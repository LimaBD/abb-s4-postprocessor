using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SprutTechnology.SCPostprocessor
{
    public partial class Postprocessor : TPostprocessor
    {
        private const int AbsoluteMaxFileSizeKb = 1024;
        private const double ExtAxisUndefined = 9E9;

        private readonly List<List<MotionPoint>> chunks = new();
        private readonly List<string> pendingComments = new();

        private bool unitsAreInches;
        private int maxFileSizeBytes;
        private string outputFolder = string.Empty;
        private string baseProgramName = "SCXS4JOB";
        private string toolName = "tool0";
        private string wobjName = "wobj0";
        private string zoneName = "z10";
        private double currentFeedMmPerMin;
        private int defaultSpeedMmPerSec = 250;

        private sealed class MotionPoint
        {
            public int IndexInChunk { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }
            public double Q1 { get; set; }
            public double Q2 { get; set; }
            public double Q3 { get; set; }
            public double Q4 { get; set; }
            public int SpeedMmPerSec { get; set; }
            public bool JointMotion { get; set; }
            public List<string> CommentsBeforeMove { get; set; } = new();
        }

        public override void OnStartProject(ICLDProject prj)
        {
            unitsAreInches = prj.Int["Units"] > 0;
            outputFolder = Settings.Params.Str["OutFiles.OutputFolder"];
            baseProgramName = SanitizeIdentifier(Settings.Params.Str["OutFiles.BaseProgramName"], "SCXS4JOB");
            toolName = SanitizeIdentifier(Settings.Params.Str["Motion.ToolName"], "tool0");
            wobjName = SanitizeIdentifier(Settings.Params.Str["Motion.WobjName"], "wobj0");
            zoneName = SanitizeIdentifier(Settings.Params.Str["Motion.ZoneName"], "z10");
            defaultSpeedMmPerSec = Settings.Params.Int["Motion.DefaultSpeedMMperSec"];
            if (defaultSpeedMmPerSec <= 0)
            {
                defaultSpeedMmPerSec = 250;
            }

            int requestedKb = Settings.Params.Int["OutFiles.MaxFileSizeKB"];
            if (requestedKb > AbsoluteMaxFileSizeKb)
            {
                Log.Warning("MaxFileSizeKB is capped to 1024 KB for S4 floppy/emulator workflows.");
                requestedKb = AbsoluteMaxFileSizeKb;
            }
            if (requestedKb < 64)
            {
                Log.Warning("MaxFileSizeKB too low; raised to 64 KB.");
                requestedKb = 64;
            }

            maxFileSizeBytes = requestedKb * 1024;
            chunks.Clear();
            pendingComments.Clear();
            currentFeedMmPerMin = defaultSpeedMmPerSec * 60.0;
            EnsureChunk();
        }

        public override void OnFinishProject(ICLDProject prj)
        {
            if (chunks.Count == 0 || (chunks.Count == 1 && chunks[0].Count == 0))
            {
                return;
            }

            var generatedModuleNames = new List<string>();
            for (int i = 0; i < chunks.Count; i++)
            {
                if (chunks[i].Count == 0)
                {
                    continue;
                }

                string text = BuildRapidModuleText(chunks[i], i);
                int bytes = Encoding.ASCII.GetByteCount(text);
                if (bytes > maxFileSizeBytes)
                {
                    Log.Error("Generated file exceeds configured max size. Lower point count per segment.");
                }

                var file = new TTextNCFile();
                file.OutputFileName = BuildOutputFileName(i);
                WriteAllLines(file, text);
                generatedModuleNames.Add(BuildModuleName(i));
            }

            if (generatedModuleNames.Count > 1)
            {
                string masterText = BuildMasterModuleText(generatedModuleNames);
                var masterFile = new TTextNCFile();
                masterFile.OutputFileName = BuildMasterOutputFileName();
                WriteAllLines(masterFile, masterText);
            }
        }

        public override void OnMoveVelocity(ICLDMoveVelocityCommand cmd, CLDArray cld)
        {
            double value = cmd.FeedValue;
            if (unitsAreInches)
            {
                value *= 25.4;
            }
            currentFeedMmPerMin = value;
        }

        public override void OnInsert(ICLDInsertCommand cmd, CLDArray cld)
        {
            if (!string.IsNullOrWhiteSpace(cmd.Text))
            {
                pendingComments.Add(cmd.Text.Trim());
            }
        }

        public override void OnFrom(ICLDFromCommand cmd, CLDArray cld)
        {
        }

        public override void OnMultiGoto(ICLDMultiGotoCommand cmd, CLDArray cld)
        {
            AddMotion(cmd, false);
        }

        public override void OnPhysicGoto(ICLDPhysicGotoCommand cmd, CLDArray cld)
        {
            AddMotion(cmd, true);
        }

        public override void OnGoHome(ICLDGoHomeCommand cmd, CLDArray cld)
        {
            AddMotion(cmd, true);
        }

        public override void OnMultiArc(ICLDMultiArcCommand cmd, CLDArray cld)
        {
            AddPointValues(cmd.EndP.P.X, cmd.EndP.P.Y, cmd.EndP.P.Z, cmd.EndP.N.A, cmd.EndP.N.B, cmd.EndP.N.C, false);
        }

        private void AddMotion(ICLDMotionCommand motion, bool jointMotion)
        {
            if (motion is ICLDMultiMotionCommand mm)
            {
                AddPointValues(mm.EP.X, mm.EP.Y, mm.EP.Z, mm.EN.A, mm.EN.B, mm.EN.C, jointMotion);
            }
        }

        private void AddLinearPoint(T3DPoint p, dynamic n)
        {
            AddPointValues(p.X, p.Y, p.Z, n.A, n.B, n.C, false);
        }

        private void AddJointPoint(T3DPoint p, dynamic n)
        {
            AddPointValues(p.X, p.Y, p.Z, n.A, n.B, n.C, true);
        }

        private void AddPointValues(double x, double y, double z, double a, double b, double c, bool jointMotion)
        {
            EnsureChunk();
            var chunk = chunks[^1];

            var q = EulerDegToQuaternion(a, b, c);
            int speed = ResolveSpeedMmPerSec(jointMotion);

            var point = new MotionPoint
            {
                IndexInChunk = chunk.Count + 1,
                X = x,
                Y = y,
                Z = z,
                Q1 = q.Q1,
                Q2 = q.Q2,
                Q3 = q.Q3,
                Q4 = q.Q4,
                SpeedMmPerSec = speed,
                JointMotion = jointMotion,
                CommentsBeforeMove = new List<string>(pendingComments)
            };
            pendingComments.Clear();

            if (chunk.Count > 0 && WillExceedFileLimit(chunk, point))
            {
                chunks.Add(new List<MotionPoint>());
                chunk = chunks[^1];
                point.IndexInChunk = 1;
            }

            chunk.Add(point);
        }

        private void EnsureChunk()
        {
            if (chunks.Count == 0)
            {
                chunks.Add(new List<MotionPoint>());
            }
        }

        private bool WillExceedFileLimit(List<MotionPoint> currentChunk, MotionPoint candidate)
        {
            var probe = new List<MotionPoint>(currentChunk) { candidate };
            string text = BuildRapidModuleText(probe, chunks.Count - 1);
            return Encoding.ASCII.GetByteCount(text) > maxFileSizeBytes;
        }

        private string BuildOutputFileName(int chunkIndex)
        {
            string suffix = chunkIndex == 0 ? string.Empty : $"_{chunkIndex + 1:000}";
            return outputFolder + @"\" + baseProgramName + suffix + ".mod";
        }

        private string BuildRapidModuleText(List<MotionPoint> points, int chunkIndex)
        {
            string moduleName = BuildModuleName(chunkIndex);
            string procName = "main";
            var sb = new StringBuilder(8192);

            sb.AppendLine($"MODULE {moduleName}");
            sb.AppendLine();
            sb.AppendLine("  ! Generated by SprutCAM X ABB S4 postprocessor");
            sb.AppendLine($"  ! Max file size limit: {AbsoluteMaxFileSizeKb} KB");
            sb.AppendLine();

            foreach (var p in points)
            {
                sb.AppendLine(
                    $"  CONST robtarget p{p.IndexInChunk}:=[[{Fmt(p.X)},{Fmt(p.Y)},{Fmt(p.Z)}],[{Fmt(p.Q1)},{Fmt(p.Q2)},{Fmt(p.Q3)},{Fmt(p.Q4)}],[0,0,0,0],[{ExtAxisUndefined.ToString("0.###E+0", CultureInfo.InvariantCulture)},{ExtAxisUndefined.ToString("0.###E+0", CultureInfo.InvariantCulture)},{ExtAxisUndefined.ToString("0.###E+0", CultureInfo.InvariantCulture)},{ExtAxisUndefined.ToString("0.###E+0", CultureInfo.InvariantCulture)},{ExtAxisUndefined.ToString("0.###E+0", CultureInfo.InvariantCulture)},{ExtAxisUndefined.ToString("0.###E+0", CultureInfo.InvariantCulture)}]];");
            }

            sb.AppendLine();
            sb.AppendLine($"  PROC {procName}()");
            foreach (var p in points)
            {
                foreach (string c in p.CommentsBeforeMove)
                {
                    sb.AppendLine($"    ! {c}");
                }

                string move = p.JointMotion ? "MoveJ" : "MoveL";
                sb.AppendLine($"    {move} p{p.IndexInChunk},v{p.SpeedMmPerSec},{zoneName},{toolName}\\WObj:={wobjName};");
            }
            sb.AppendLine("  ENDPROC");
            sb.AppendLine();
            sb.AppendLine("ENDMODULE");

            return sb.ToString();
        }

        private string BuildMasterModuleText(List<string> moduleNames)
        {
            string masterModuleName = BuildMasterModuleName();
            var sb = new StringBuilder(2048);

            sb.AppendLine($"MODULE {masterModuleName}");
            sb.AppendLine();
            sb.AppendLine("  ! Generated by SprutCAM X ABB S4 postprocessor");
            sb.AppendLine("  ! Master routine executing split module chunks in order");
            sb.AppendLine();
            sb.AppendLine("  PROC main()");
            foreach (string moduleName in moduleNames)
            {
                sb.AppendLine($"    {moduleName}\\main;");
            }
            sb.AppendLine("  ENDPROC");
            sb.AppendLine();
            sb.AppendLine("ENDMODULE");

            return sb.ToString();
        }

        private string BuildModuleName(int chunkIndex)
        {
            string suffix = chunkIndex == 0 ? string.Empty : $"_{chunkIndex + 1:000}";
            string module = baseProgramName + suffix;
            if (module.Length > 32)
            {
                module = module[..32];
            }
            return module;
        }

        private string BuildMasterModuleName()
        {
            string module = baseProgramName + "_MASTER";
            if (module.Length > 32)
            {
                module = module[..32];
            }
            return module;
        }

        private string BuildMasterOutputFileName()
        {
            return outputFolder + @"\" + baseProgramName + "_MASTER.mod";
        }

        private static (double Q1, double Q2, double Q3, double Q4) EulerDegToQuaternion(double aDeg, double bDeg, double cDeg)
        {
            double ax = DegreesToRadians(aDeg) * 0.5;
            double ay = DegreesToRadians(bDeg) * 0.5;
            double az = DegreesToRadians(cDeg) * 0.5;

            double cx = Math.Cos(ax);
            double sx = Math.Sin(ax);
            double cy = Math.Cos(ay);
            double sy = Math.Sin(ay);
            double cz = Math.Cos(az);
            double sz = Math.Sin(az);

            double qw = (cx * cy * cz) + (sx * sy * sz);
            double qx = (sx * cy * cz) - (cx * sy * sz);
            double qy = (cx * sy * cz) + (sx * cy * sz);
            double qz = (cx * cy * sz) - (sx * sy * cz);

            double norm = Math.Sqrt((qw * qw) + (qx * qx) + (qy * qy) + (qz * qz));
            if (norm <= double.Epsilon)
            {
                return (1, 0, 0, 0);
            }

            return (qw / norm, qx / norm, qy / norm, qz / norm);
        }

        private int ResolveSpeedMmPerSec(bool jointMotion)
        {
            if (jointMotion)
            {
                return 500;
            }

            if (currentFeedMmPerMin <= 0)
            {
                return defaultSpeedMmPerSec;
            }

            int speed = (int)Math.Round(currentFeedMmPerMin / 60.0, MidpointRounding.AwayFromZero);
            if (speed <= 0)
            {
                speed = defaultSpeedMmPerSec;
            }
            return speed;
        }

        private static double DegreesToRadians(double value)
        {
            return value * (Math.PI / 180.0);
        }

        private static string SanitizeIdentifier(string source, string fallback)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return fallback;
            }

            var sb = new StringBuilder(source.Length);
            foreach (char c in source.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    sb.Append(c);
                }
            }

            if (sb.Length == 0)
            {
                return fallback;
            }
            if (char.IsDigit(sb[0]))
            {
                sb.Insert(0, '_');
            }
            return sb.ToString();
        }

        private static string Fmt(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static void WriteAllLines(TTextNCFile file, string text)
        {
            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            foreach (string line in lines)
            {
                file.Output(line);
            }
        }
    }
}
