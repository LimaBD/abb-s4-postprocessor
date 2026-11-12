#!/usr/bin/env python3
import argparse
import json
import math
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List, Optional, Tuple


ROBTARGET_RE = re.compile(
    r"^\s*(?:PERS|VAR|CONST)\s+robtarget\s+([A-Za-z_]\w*)\s*:=\s*\[\[([^\]]+)\],",
    re.IGNORECASE,
)
MOVE_RE = re.compile(r"^\s*(Move[JLCP])\s+(.+?)\s*;\s*$", re.IGNORECASE)
INLINE_POINT_RE = re.compile(r"\[\[([^\]]+)\],")


@dataclass
class Point3D:
    x: float
    y: float
    z: float


@dataclass
class Move:
    move_type: str
    target_name: str
    speed: Optional[str]
    point: Point3D
    line_number: int


def parse_xyz(text: str) -> Point3D:
    values = [v.strip() for v in text.split(",")]
    if len(values) < 3:
        raise ValueError(f"Invalid XYZ text: {text}")
    return Point3D(float(values[0]), float(values[1]), float(values[2]))


def parse_robtargets(lines: List[str]) -> Dict[str, Point3D]:
    targets: Dict[str, Point3D] = {}
    for line in lines:
        match = ROBTARGET_RE.match(line)
        if not match:
            continue
        name = match.group(1)
        xyz_text = match.group(2)
        targets[name] = parse_xyz(xyz_text)
    return targets


def parse_move_args(arg_text: str) -> Tuple[str, Optional[str]]:
    parts = [p.strip() for p in arg_text.split(",")]
    target_name = parts[0] if parts else ""
    speed = parts[1] if len(parts) > 1 else None
    return target_name, speed


def resolve_target_point(target: str, robtargets: Dict[str, Point3D]) -> Optional[Point3D]:
    if target in robtargets:
        return robtargets[target]
    inline = INLINE_POINT_RE.search(target)
    if inline:
        return parse_xyz(inline.group(1))
    return None


def parse_moves(lines: List[str], robtargets: Dict[str, Point3D]) -> Tuple[List[Move], List[str]]:
    moves: List[Move] = []
    warnings: List[str] = []
    for idx, line in enumerate(lines, start=1):
        match = MOVE_RE.match(line)
        if not match:
            continue
        move_type = match.group(1)
        target_name, speed = parse_move_args(match.group(2))
        point = resolve_target_point(target_name, robtargets)
        if point is None:
            warnings.append(f"Line {idx}: target '{target_name}' not resolved.")
            continue
        moves.append(
            Move(
                move_type=move_type,
                target_name=target_name,
                speed=speed,
                point=point,
                line_number=idx,
            )
        )
    return moves, warnings


def distance(a: Point3D, b: Point3D) -> float:
    return math.sqrt((a.x - b.x) ** 2 + (a.y - b.y) ** 2 + (a.z - b.z) ** 2)


def angle_deg(a: Point3D, b: Point3D, c: Point3D) -> float:
    v1 = (a.x - b.x, a.y - b.y, a.z - b.z)
    v2 = (c.x - b.x, c.y - b.y, c.z - b.z)
    m1 = math.sqrt(v1[0] ** 2 + v1[1] ** 2 + v1[2] ** 2)
    m2 = math.sqrt(v2[0] ** 2 + v2[1] ** 2 + v2[2] ** 2)
    if m1 == 0 or m2 == 0:
        return 0.0
    dot = v1[0] * v2[0] + v1[1] * v2[1] + v1[2] * v2[2]
    cang = max(-1.0, min(1.0, dot / (m1 * m2)))
    return math.degrees(math.acos(cang))


def analyze_safety(
    moves: List[Move],
    max_segment_mm: float,
    max_jump_mm: float,
    min_z_mm: Optional[float],
    workspace: Optional[Tuple[float, float, float, float, float, float]],
    sharp_turn_deg: float,
) -> List[str]:
    warnings: List[str] = []
    if len(moves) < 2:
        warnings.append("Not enough motion points to analyze path continuity.")
        return warnings

    for i in range(1, len(moves)):
        d = distance(moves[i - 1].point, moves[i].point)
        if d > max_segment_mm:
            warnings.append(
                f"Long segment at move #{i+1} (line {moves[i].line_number}): {d:.2f} mm > {max_segment_mm:.2f} mm."
            )
        if moves[i].move_type.lower() == "movej" and d > max_jump_mm:
            warnings.append(
                f"Large MoveJ jump at move #{i+1} (line {moves[i].line_number}): {d:.2f} mm > {max_jump_mm:.2f} mm."
            )

    if min_z_mm is not None:
        for i, mv in enumerate(moves, start=1):
            if mv.point.z < min_z_mm:
                warnings.append(
                    f"Low Z at move #{i} (line {mv.line_number}): z={mv.point.z:.2f} mm below {min_z_mm:.2f} mm."
                )

    if workspace:
        xmin, xmax, ymin, ymax, zmin, zmax = workspace
        for i, mv in enumerate(moves, start=1):
            p = mv.point
            if not (xmin <= p.x <= xmax and ymin <= p.y <= ymax and zmin <= p.z <= zmax):
                warnings.append(
                    f"Out-of-workspace point at move #{i} (line {mv.line_number}): "
                    f"({p.x:.2f}, {p.y:.2f}, {p.z:.2f}) mm."
                )

    for i in range(1, len(moves) - 1):
        a = moves[i - 1].point
        b = moves[i].point
        c = moves[i + 1].point
        if distance(a, b) < 1e-6 or distance(b, c) < 1e-6:
            continue
        corner_angle = angle_deg(a, b, c)
        path_turn = abs(180.0 - corner_angle)
        if path_turn > sharp_turn_deg:
            warnings.append(
                f"Sharp turn around move #{i+1} (line {moves[i].line_number}): {path_turn:.2f} deg."
            )

    return warnings


def build_html(moves: List[Move], source_name: str, output_html: Path) -> None:
    points = [{"x": m.point.x, "y": m.point.y, "z": m.point.z, "type": m.move_type} for m in moves]
    data_json = json.dumps(points, separators=(",", ":"))
    title = f"RAPID Path Viewer - {source_name}"
    html = f"""<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>{title}</title>
  <style>
    body {{ margin: 0; font-family: Arial, sans-serif; background: #111; color: #eee; }}
    #ui {{
      position: fixed; top: 0; left: 0; right: 0; z-index: 10;
      background: rgba(0, 0, 0, 0.75); padding: 10px 14px; display: flex; gap: 12px; align-items: center; flex-wrap: wrap;
    }}
    #viewer {{ position: fixed; top: 56px; left: 0; right: 0; bottom: 0; }}
    input[type=range] {{ width: 320px; }}
    .pill {{ border: 1px solid #333; padding: 2px 8px; border-radius: 999px; font-size: 12px; }}
  </style>
</head>
<body>
  <div id="ui">
    <button id="playBtn">Play</button>
    <button id="resetBtn">Reset</button>
    <label>Speed <input id="speed" type="range" min="0.1" max="5" value="1" step="0.1" /></label>
    <label>Point <input id="frame" type="range" min="0" max="0" value="0" step="1" /></label>
    <span class="pill">Axis map: ABB(X,Y,Z) -> Scene(X,Z,-Y)</span>
    <span class="pill">View: Left-drag rotate | Wheel zoom | Right-drag pan</span>
    <span id="info" class="pill"></span>
  </div>
  <div id="viewer"></div>

  <script src="https://unpkg.com/three@0.160.0/build/three.min.js"></script>
  <script>
    if (!window.THREE) {{
      document.getElementById('viewer').innerHTML =
        '<div style="padding:20px;color:#ff8a8a">Failed to load 3D engine (Three.js). Check internet access or network blocking.</div>';
      throw new Error('Three.js not loaded');
    }}
    const points = {data_json};
    const viewer = document.getElementById('viewer');
    const frameInput = document.getElementById('frame');
    const speedInput = document.getElementById('speed');
    const playBtn = document.getElementById('playBtn');
    const resetBtn = document.getElementById('resetBtn');
    const info = document.getElementById('info');

    if (!points.length) {{
      viewer.innerHTML = '<div style="padding:20px">No points parsed.</div>';
      throw new Error('No points parsed.');
    }}

    const scene = new THREE.Scene();
    scene.background = new THREE.Color(0x111111);
    const camera = new THREE.PerspectiveCamera(60, viewer.clientWidth / viewer.clientHeight, 0.1, 100000);
    const renderer = new THREE.WebGLRenderer({{ antialias: true }});
    renderer.setSize(viewer.clientWidth, viewer.clientHeight);
    viewer.appendChild(renderer.domElement);

    const ambient = new THREE.AmbientLight(0xffffff, 0.55);
    scene.add(ambient);
    const dirLight = new THREE.DirectionalLight(0xffffff, 0.7);
    dirLight.position.set(1, 1, 1);
    scene.add(dirLight);

    // ABB frame is Z-up. Three.js is Y-up.
    // Mapping keeps right-handed orientation:
    //   sceneX = abbX
    //   sceneY = abbZ (vertical)
    //   sceneZ = -abbY
    function abbToScene(p) {{
      return new THREE.Vector3(p.x, p.z, -p.y);
    }}

    const vectors = points.map(p => abbToScene(p));
    const bbox = new THREE.Box3().setFromPoints(vectors);
    const center = bbox.getCenter(new THREE.Vector3());
    const size = bbox.getSize(new THREE.Vector3());
    const radius = Math.max(size.x, size.y, size.z) || 1;
    const gridSize = Math.max(100, Math.ceil(radius / 50) * 100);
    const grid = new THREE.GridHelper(gridSize, Math.max(10, Math.floor(gridSize / 20)));
    scene.add(grid);

    const axes = new THREE.AxesHelper(Math.max(50, radius * 0.2));
    axes.position.copy(center);
    scene.add(axes);

    const lineGeo = new THREE.BufferGeometry().setFromPoints(vectors);
    const lineMat = new THREE.LineBasicMaterial({{ color: 0x44b9ff }});
    const fullLine = new THREE.Line(lineGeo, lineMat);
    scene.add(fullLine);

    const traceGeo = new THREE.BufferGeometry();
    traceGeo.setAttribute('position', new THREE.BufferAttribute(new Float32Array(vectors.length * 3), 3));
    traceGeo.setDrawRange(0, 1);
    const traceMat = new THREE.LineBasicMaterial({{ color: 0x11ff88, linewidth: 2 }});
    const trace = new THREE.Line(traceGeo, traceMat);
    scene.add(trace);

    const marker = new THREE.Mesh(
      new THREE.SphereGeometry(Math.max(radius * 0.01, 2), 20, 20),
      new THREE.MeshStandardMaterial({{ color: 0xff6633 }})
    );
    scene.add(marker);

    const total = vectors.length - 1;
    frameInput.max = String(total);
    frameInput.value = "0";
    let frame = 0;
    let playing = false;
    let lastTs = performance.now();
    let acc = 0;
    const fpsBase = 30;

    const tracePos = traceGeo.getAttribute('position');
    function updateFrame(newFrame) {{
      frame = Math.max(0, Math.min(total, Math.floor(newFrame)));
      const pScene = vectors[frame];
      const pRaw = points[frame];
      marker.position.copy(pScene);
      for (let i = 0; i <= frame; i++) {{
        const v = vectors[i];
        tracePos.setXYZ(i, v.x, v.y, v.z);
      }}
      traceGeo.setDrawRange(0, frame + 1);
      tracePos.needsUpdate = true;
      frameInput.value = String(frame);
      const t = points[frame].type;
      info.textContent = `Point ${{frame + 1}} / ${{vectors.length}} | ${{t}} | x=${{pRaw.x.toFixed(2)}} y=${{pRaw.y.toFixed(2)}} z=${{pRaw.z.toFixed(2)}} mm`;
    }}

    frameInput.addEventListener('input', () => updateFrame(Number(frameInput.value)));
    playBtn.addEventListener('click', () => {{
      playing = !playing;
      playBtn.textContent = playing ? 'Pause' : 'Play';
    }});
    resetBtn.addEventListener('click', () => {{
      playing = false;
      playBtn.textContent = 'Play';
      updateFrame(0);
    }});

    let orbitYaw = Math.PI * 0.25;
    let orbitPitch = 0.55;
    let orbitDist = Math.max(radius * 2.0, 120);
    const panOffset = new THREE.Vector3(0, 0, 0);

    function updateCamera() {{
      const target = center.clone().add(panOffset);
      const cp = Math.cos(orbitPitch);
      const x = target.x + orbitDist * cp * Math.cos(orbitYaw);
      const y = target.y + orbitDist * Math.sin(orbitPitch);
      const z = target.z + orbitDist * cp * Math.sin(orbitYaw);
      camera.position.set(x, y, z);
      camera.lookAt(target);
    }}

    const canvas = renderer.domElement;
    canvas.style.touchAction = 'none';
    canvas.addEventListener('contextmenu', (e) => e.preventDefault());

    const dragState = {{
      active: false,
      mode: 'rotate',
      lastX: 0,
      lastY: 0,
    }};

    canvas.addEventListener('pointerdown', (e) => {{
      dragState.active = true;
      dragState.mode = (e.button === 2 || e.button === 1) ? 'pan' : 'rotate';
      dragState.lastX = e.clientX;
      dragState.lastY = e.clientY;
      canvas.setPointerCapture(e.pointerId);
    }});

    canvas.addEventListener('pointermove', (e) => {{
      if (!dragState.active) return;
      const dx = e.clientX - dragState.lastX;
      const dy = e.clientY - dragState.lastY;
      dragState.lastX = e.clientX;
      dragState.lastY = e.clientY;

      if (dragState.mode === 'rotate') {{
        orbitYaw -= dx * 0.005;
        orbitPitch -= dy * 0.005;
        const lim = Math.PI * 0.49;
        orbitPitch = Math.max(-lim, Math.min(lim, orbitPitch));
      }} else {{
        const panSpeed = orbitDist * 0.0012;
        const fwd = new THREE.Vector3();
        const right = new THREE.Vector3();
        const up = new THREE.Vector3();
        camera.getWorldDirection(fwd);
        right.crossVectors(fwd, camera.up).normalize();
        up.copy(camera.up).normalize();
        panOffset.addScaledVector(right, -dx * panSpeed);
        panOffset.addScaledVector(up, dy * panSpeed);
      }}
    }});

    function stopDrag(e) {{
      if (!dragState.active) return;
      dragState.active = false;
      try {{
        canvas.releasePointerCapture(e.pointerId);
      }} catch (_) {{}}
    }}
    canvas.addEventListener('pointerup', stopDrag);
    canvas.addEventListener('pointercancel', stopDrag);
    canvas.addEventListener('pointerleave', stopDrag);

    canvas.addEventListener('wheel', (e) => {{
      e.preventDefault();
      const factor = Math.exp(e.deltaY * 0.0012);
      orbitDist *= factor;
      orbitDist = Math.max(radius * 0.2, Math.min(radius * 30, orbitDist));
    }}, {{ passive: false }});

    updateCamera();
    updateFrame(0);

    window.addEventListener('resize', () => {{
      camera.aspect = viewer.clientWidth / viewer.clientHeight;
      camera.updateProjectionMatrix();
      renderer.setSize(viewer.clientWidth, viewer.clientHeight);
    }});

    function animate(ts) {{
      const delta = (ts - lastTs) / 1000;
      lastTs = ts;
      if (playing) {{
        const speed = Number(speedInput.value);
        acc += delta * fpsBase * speed;
        if (acc >= 1) {{
          const step = Math.floor(acc);
          acc -= step;
          updateFrame(frame + step);
          if (frame >= total) {{
            playing = false;
            playBtn.textContent = 'Play';
          }}
        }}
      }}
      updateCamera();
      renderer.render(scene, camera);
      requestAnimationFrame(animate);
    }}
    requestAnimationFrame(animate);
  </script>
</body>
</html>
"""
    output_html.write_text(html, encoding="utf-8")


def summarize(moves: List[Move]) -> Dict[str, object]:
    if not moves:
        return {"point_count": 0}
    xs = [m.point.x for m in moves]
    ys = [m.point.y for m in moves]
    zs = [m.point.z for m in moves]
    total = 0.0
    for i in range(1, len(moves)):
        total += distance(moves[i - 1].point, moves[i].point)
    move_types: Dict[str, int] = {}
    for m in moves:
        move_types[m.move_type] = move_types.get(m.move_type, 0) + 1
    return {
        "point_count": len(moves),
        "move_counts": move_types,
        "path_length_mm": round(total, 3),
        "bounds_mm": {
            "x_min": round(min(xs), 3),
            "x_max": round(max(xs), 3),
            "y_min": round(min(ys), 3),
            "y_max": round(max(ys), 3),
            "z_min": round(min(zs), 3),
            "z_max": round(max(zs), 3),
        },
    }


def parse_workspace(args: argparse.Namespace) -> Optional[Tuple[float, float, float, float, float, float]]:
    if args.workspace is None:
        return None
    parts = [p.strip() for p in args.workspace.split(",")]
    if len(parts) != 6:
        raise ValueError("--workspace must have 6 comma-separated values: xmin,xmax,ymin,ymax,zmin,zmax")
    vals = tuple(float(p) for p in parts)
    return vals  # type: ignore[return-value]


def run() -> int:
    parser = argparse.ArgumentParser(
        description="Parse ABB RAPID .mod/.prg files and generate 3D path animation + safety report."
    )
    parser.add_argument("input", help="RAPID file path (.mod/.prg)")
    parser.add_argument("--out-html", default="out/rapid_path_view.html", help="Output HTML file")
    parser.add_argument("--out-report", default="out/rapid_path_report.json", help="Output JSON report")
    parser.add_argument("--max-segment-mm", type=float, default=200.0, help="Warn if segment is longer than this")
    parser.add_argument("--max-jump-mm", type=float, default=400.0, help="Warn if MoveJ jump is longer than this")
    parser.add_argument("--min-z-mm", type=float, default=None, help="Warn if Z is lower than this")
    parser.add_argument(
        "--workspace",
        default=None,
        help="Optional machine workspace limits as xmin,xmax,ymin,ymax,zmin,zmax (mm)",
    )
    parser.add_argument("--sharp-turn-deg", type=float, default=90.0, help="Warn on turns sharper than this")
    args = parser.parse_args()

    source = Path(args.input)
    if not source.exists():
        raise FileNotFoundError(f"Input file not found: {source}")

    lines = source.read_text(encoding="utf-8", errors="replace").splitlines()
    robtargets = parse_robtargets(lines)
    moves, parse_warnings = parse_moves(lines, robtargets)
    if not moves:
        raise RuntimeError("No valid Move instructions were parsed from file.")

    workspace = parse_workspace(args)
    safety_warnings = analyze_safety(
        moves=moves,
        max_segment_mm=args.max_segment_mm,
        max_jump_mm=args.max_jump_mm,
        min_z_mm=args.min_z_mm,
        workspace=workspace,
        sharp_turn_deg=args.sharp_turn_deg,
    )

    report = {
        "input_file": str(source),
        "summary": summarize(moves),
        "parse_warnings": parse_warnings,
        "safety_warnings": safety_warnings,
    }

    out_html = Path(args.out_html)
    out_report = Path(args.out_report)
    out_html.parent.mkdir(parents=True, exist_ok=True)
    out_report.parent.mkdir(parents=True, exist_ok=True)
    build_html(moves, source.name, out_html)
    out_report.write_text(json.dumps(report, indent=2), encoding="utf-8")

    print(f"Parsed moves: {report['summary']['point_count']}")
    print(f"HTML viewer: {out_html.resolve()}")
    print(f"JSON report: {out_report.resolve()}")
    print(f"Parse warnings: {len(parse_warnings)}")
    print(f"Safety warnings: {len(safety_warnings)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(run())
