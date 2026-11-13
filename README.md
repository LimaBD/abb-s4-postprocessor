![abb robots](./abb-robots.jpg)

# ABB S4 SprutCAM X Toolkit

This repository contains two related tools for ABB S4 workflows with SprutCAM X Robot:

- **Postprocessor**: generates ABB S4-oriented RAPID `.mod` files from SprutCAM output.
- **Viewer**: visualizes generated RAPID paths in 3D and creates a safety-oriented JSON report.

## Repository structure

- `postprocessor/` – SprutCAM X .NET postprocessor project and configuration
- `postprocessor/tests/` – unit tests for postprocessor logic
- `viewer/` – RAPID 3D path viewer (Python)
- `sample/` – sample RAPID files

## Component documentation

- Postprocessor docs: [`postprocessor/README.md`](./postprocessor/README.md)
- Viewer docs: [`viewer/README.md`](./viewer/README.md)

## Quick start

### 1) Build the postprocessor

```powershell
dotnet build .\postprocessor\ABB_S4_SprutCAMX.csproj
```

### 2) Run postprocessor tests

```powershell
dotnet test .\postprocessor\tests\AbbS4Postprocessor.Tests\AbbS4Postprocessor.Tests.csproj
```

### 3) Run the viewer

```powershell
python .\viewer\rapid_path_viewer.py .\sample\SCXS4JOB.mod `
  --out-html .\viewer\out\SCXS4JOB_path.html `
  --out-report .\viewer\out\SCXS4JOB_report.json
```
