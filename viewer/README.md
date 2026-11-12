# ABB RAPID Path Viewer (3D + animation)

This tool parses ABB RAPID programs (`.mod`/`.prg`) and generates:

- an interactive **3D animated path viewer** (HTML),
- a **JSON safety report** with warnings.

It was made to validate CAM-generated paths before running on an ABB controller (including S4 generation programs like IRB 1400 M94A jobs).

## Requirements

- Python 3.8+ (no extra package needed)
- Internet access when opening the HTML (it loads `three.js` from CDN)

## Usage

```powershell
python .\rapid_path_viewer.py .\SCXS4JOB.mod `
  --out-html .\out\SCXS4JOB_path.html `
  --out-report .\out\SCXS4JOB_report.json `
  --max-segment-mm 200 `
  --max-jump-mm 400 `
  --sharp-turn-deg 90
```

Optional safety limits:

- `--min-z-mm <value>`: warns if Z goes below this value.
- `--workspace xmin,xmax,ymin,ymax,zmin,zmax`: warns for points outside allowed workspace.

Example:

```powershell
python .\rapid_path_viewer.py .\SCXS4JOB.mod `
  --workspace -100,800,-800,800,0,1200 `
  --min-z-mm 0
```

## Output

- HTML viewer:
  - Full 3D path line.
  - Animated marker moving point-by-point.
  - Play/Pause, Reset, speed control, frame slider.
  - Camera controls: left-drag rotate, mouse wheel zoom, right-drag pan.
  - XYZ position display for each point.
  - Axis mapping note shown in UI:
    - `ABB(X,Y,Z) -> Scene(X,Z,-Y)` (so ABB `Z` is vertical on screen).
- JSON report:
  - Total points and path length.
  - Bounding box (X/Y/Z min-max).
  - Parse warnings.
  - Safety warnings:
    - long segments,
    - large `MoveJ` jumps,
    - sharp turns,
    - low Z (if configured),
    - outside workspace (if configured).

## Notes

- This checks **path geometry and simple safety heuristics**, not full robot dynamics/kinematics or collisions with detailed 3D machine/tool models.
- For final validation, still run ABB simulation/offline programming checks and proper cell-level collision checks.
- ABB documentation describes world coordinates as the floor reference for other coordinate systems; this viewer now renders with ABB-style Z-up behavior.

## Troubleshooting

- If the HTML looks black or empty, regenerate it and reopen:
- If the HTML looks black or empty, regenerate it and reopen:

```powershell
python .\rapid_path_viewer.py .\SCXS4JOB.mod --out-html .\out\SCXS4JOB_path.html --out-report .\out\SCXS4JOB_report.json
```

- If you see **"Failed to load 3D engine (Three.js)"**, your network is blocking CDN scripts. Allow access to `unpkg.com` or use another network.
