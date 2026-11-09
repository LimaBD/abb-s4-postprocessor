# ABB S4 postprocessor for SprutCAM X Robot

This folder contains a custom **SprutCAM X .NET postprocessor** that outputs **ABB S4-oriented RAPID `.mod` files** for robots such as **IRB 1400 M94A**.

## What this post does

- Generates RAPID modules (`.mod`) with:
  - `MODULE ... ENDMODULE`
  - `CONST robtarget pN := ...`
  - `PROC main()`
  - `MoveL` / `MoveJ` instructions
- Converts SprutCAM XYZ + A/B/C orientation to quaternion robtarget orientation.
- Splits output into multiple `.mod` files automatically.
- Enforces file size with a **hard cap of 1024 KB** per file (floppy/emulator requirement).

## Internet research used

- SprutCAM X postprocessor examples and structure:
  - https://github.com/SprutCAMTech/SprutCAM-postprocessors-examples
  - https://kb.sprutcam.com/docs/Inp.Net/16/en/articles/inpcoreorganization/setoffiles.html
  - https://sprutcam.com/postprocessors/
- ABB RAPID module/program structure references:
  - https://underautomation.com/abb/documentation/rws-rapid-modules
  - https://robodk.com/post/ABB-RAPID-S4C

## Files in this folder

- `ABB_S4_SprutCAMX.csproj` – project file
- `Postprocessor.cs` – main postprocessor logic
- `Settings.xml` – user parameters shown in SprutCAM X
- `PostSchema.xsd` – schema for `Settings.xml`

## How to import into SprutCAM X Robot

1. Build the project (`Release` or `Debug`) and locate the generated DLL:
   - `bin\Debug\ABB_S4_SprutCAMX.dll` (or `bin\Release\...`)
2. In SprutCAM X Robot, open your robot project and go to the postprocessor selection for the machine/operation.
3. Choose to add/load a **.NET postprocessor** and select `ABB_S4_SprutCAMX.dll`.
4. Open postprocessor parameters and set:
   - `OutFiles.OutputFolder`
   - `OutFiles.BaseProgramName`
   - `OutFiles.MaxFileSizeKB` (keep `<= 1024` for floppy/emulator workflow)
   - `Motion.ToolName` and `Motion.WobjName` to names that exist on the S4 controller.
5. Run postprocessing and verify generated `.mod` files in the output folder.
6. Transfer files to floppy/emulator and load on the ABB S4 controller.

### If SprutCAM does not show the postprocessor

- Confirm SprutCAM and the postprocessor use compatible architecture/runtime.
- Rebuild the DLL and reload SprutCAM.
- Make sure SprutCAM dependencies (`SCPostprocessor.dll`, `STDefLib.dll`, `VecMatrLib.dll`) are available in your SprutCAM installation.

## Important parameters (`Settings.xml`)

- `OutFiles.OutputFolder`: target folder for generated `.mod` files
- `OutFiles.BaseProgramName`: base program/module name
- `OutFiles.MaxFileSizeKB`: maximum file size in KB (hard-capped to 1024)
- `Motion.DefaultSpeedMMperSec`: fallback speed
- `Motion.ZoneName`: RAPID zone (e.g. `fine`, `z1`, `z10`)
- `Motion.ToolName`: controller tooldata name (e.g. `tool0`)
- `Motion.WobjName`: controller wobjdata name (e.g. `wobj0`)

## Notes for ABB S4 target

- RAPID dialect differences can exist between controller generations.
- This post is intentionally conservative (MoveL/MoveJ + robtarget + existing tool/wobj names).
- Validate with a small job first on robot/simulator and adjust:
  - orientation convention (A/B/C to quaternion)
  - zone/speed defaults
  - tool/workobject names existing on the controller
