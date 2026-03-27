using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

namespace Guncon3Console.TetherScript
{
    internal static class RelMouseProbe
    {
        private sealed class Candidate
        {
            public string Name;
            public byte[] Bytes;
        }

        public static int Run(string[] args)
        {
            Console.WriteLine("[RelMouseProbe] Connecting to TetherScript RelMouse...");

            var hid = new HIDController();
            hid.OnLog += (_, e) => { try { Console.WriteLine("[RelMouseProbe] " + e.Msg); } catch { } };
            hid.VendorID = (ushort)DriversConst.TTC_VENDORID;
            hid.ProductID = (ushort)DriversConst.TTC_PRODUCTID_MOUSEREL;
            hid.Connect();

            if (!hid.Connected)
            {
                Console.WriteLine("[RelMouseProbe] ERROR: Could not connect to RelMouse (VID/PID not found?)");
                return 2;
            }

            Console.WriteLine("[RelMouseProbe] Connected.");
            Console.WriteLine("[RelMouseProbe] This will send small controlled movements.");
            Console.WriteLine("[RelMouseProbe] Put the mouse cursor somewhere visible.");
            Console.WriteLine();

            try
            {
                int stepDelayMs = GetIntArg(args, "--delay", 250);
                int delta = GetIntArg(args, "--delta", 40);
                int afterEachMs = GetIntArg(args, "--aftereach", 250);
                int kp = GetIntArg(args, "--kp", 1);
                int settleMs = GetIntArg(args, "--settle", 800);

                var candidates = BuildCandidates();

                Console.WriteLine("[RelMouseProbe] Candidates: " + candidates.Count);
                Console.WriteLine("[RelMouseProbe] For each candidate, press:");
                Console.WriteLine("  ENTER = send test pattern");
                Console.WriteLine("  S     = skip");
                Console.WriteLine("  Q     = quit");
                Console.WriteLine("  A     = auto-run all candidates (prints observed cursor deltas)");
                Console.WriteLine("  T     = target/servo test (center + corners) for current candidate");
                Console.WriteLine();

                int idx = 0;
                foreach (var c in candidates)
                {
                    idx++;
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[{idx}/{candidates.Count}] {c.Name}  bytes={c.Bytes.Length}");
                    Console.ResetColor();
                    Console.WriteLine("  " + BitConverter.ToString(c.Bytes));

                    while (true)
                    {
                        var k = Console.ReadKey(true);
                        if (k.Key == ConsoleKey.Q)
                            return 0;
                        if (k.Key == ConsoleKey.A)
                        {
                            AutoRun(hid, candidates, delta, stepDelayMs, afterEachMs);
                            return 0;
                        }
                        if (k.Key == ConsoleKey.S)
                            break;
                        if (k.Key == ConsoleKey.T)
                        {
                            TargetTest(hid, c.Bytes, kp, settleMs);
                            CenterCursor();
                            System.Threading.Thread.Sleep(afterEachMs);
                            break;
                        }
                        if (k.Key == ConsoleKey.Enter)
                        {
                            SendPattern(hid, c.Bytes, delta, stepDelayMs);
                            var score = MeasurePattern(hid, c.Bytes, delta, stepDelayMs, dryRun: false);
                            Console.WriteLine($"  Observed cursor delta sum: dx={score.SumDx} dy={score.SumDy} movedSteps={score.MovedSteps}/{score.TotalSteps}");
                            CenterCursor();
                            System.Threading.Thread.Sleep(afterEachMs);
                            break;
                        }
                    }

                    Console.WriteLine();
                }

                Console.WriteLine("[RelMouseProbe] Done.");
                return 0;
            }
            finally
            {
                try { hid.Disconnect(); } catch { }
            }
        }

        private static void AutoRun(HIDController hid, List<Candidate> candidates, int delta, int stepDelayMs, int afterEachMs)
        {
            Console.WriteLine("[RelMouseProbe] Auto-run starting...");
            Console.WriteLine("[RelMouseProbe] Keep the cursor somewhere it can move (not stuck at screen edge).");

            int idx = 0;
            foreach (var c in candidates)
            {
                idx++;
                var score = MeasurePattern(hid, c.Bytes, delta, stepDelayMs, dryRun: true);

                // Print only candidates that actually moved the cursor in at least one step.
                if (score.MovedSteps > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[{idx}/{candidates.Count}] HIT {c.Name} bytes={c.Bytes.Length}  movedSteps={score.MovedSteps}/{score.TotalSteps}  sum(dx,dy)=({score.SumDx},{score.SumDy})");
                    Console.ResetColor();
                    Console.WriteLine("  " + BitConverter.ToString(c.Bytes));
                }

                CenterCursor();
                System.Threading.Thread.Sleep(afterEachMs);
            }

            Console.WriteLine("[RelMouseProbe] Auto-run done.");
        }

        private sealed class PatternScore
        {
            public int TotalSteps;
            public int MovedSteps;
            public int SumDx;
            public int SumDy;
        }

        private static PatternScore MeasurePattern(HIDController hid, byte[] baseBuf, int delta, int stepDelayMs, bool dryRun)
        {
            var score = new PatternScore();

            var steps = new List<byte[]>();
            steps.Add(WithDelta(baseBuf, dx: +delta, dy: 0));
            steps.Add(WithDelta(baseBuf, dx: -delta, dy: 0));
            steps.Add(WithDelta(baseBuf, dx: 0, dy: +delta));
            steps.Add(WithDelta(baseBuf, dx: 0, dy: -delta));

            foreach (var s in steps)
            {
                score.TotalSteps++;

                var before = GetCursorPosSafe();
                TrySend(hid, s);
                System.Threading.Thread.Sleep(stepDelayMs);
                var after = GetCursorPosSafe();

                int dxObs = after.X - before.X;
                int dyObs = after.Y - before.Y;

                if (dxObs != 0 || dyObs != 0)
                    score.MovedSteps++;

                score.SumDx += dxObs;
                score.SumDy += dyObs;

                if (!dryRun)
                    Console.WriteLine($"    step moved: ({dxObs},{dyObs})  cursor=({after.X},{after.Y})");
            }

            return score;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        private static void CenterCursor()
        {
            try
            {
                int cx = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Left + (System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width / 2);
                int cy = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Top + (System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height / 2);
                SetCursorPos(cx, cy);
            }
            catch { }
        }

        private static POINT GetCursorPosSafe()
        {
            try
            {
                POINT p;
                if (GetCursorPos(out p))
                    return p;
            }
            catch { }
            return new POINT();
        }

        private static void SendPattern(HIDController hid, byte[] baseBuf, int delta, int stepDelayMs)
        {
            // Pattern:
            //  - move right
            //  - move left
            //  - move down
            //  - move up
            //  - left click press/release (if there is a Buttons field at all)

            // We never know which byte is "buttons". For click testing, we just also send
            // a variant where the 3rd byte is toggled to 1 and then back to 0.

            var steps = new List<byte[]>();
            steps.Add(WithDelta(baseBuf, dx: +delta, dy: 0));
            steps.Add(WithDelta(baseBuf, dx: -delta, dy: 0));
            steps.Add(WithDelta(baseBuf, dx: 0, dy: +delta));
            steps.Add(WithDelta(baseBuf, dx: 0, dy: -delta));

            foreach (var s in steps)
            {
                TrySend(hid, s);
                System.Threading.Thread.Sleep(stepDelayMs);
            }

            var clickDown = (byte[])baseBuf.Clone();
            if (clickDown.Length >= 3) clickDown[2] = 1;
            TrySend(hid, clickDown);
            System.Threading.Thread.Sleep(stepDelayMs);

            var clickUp = (byte[])baseBuf.Clone();
            if (clickUp.Length >= 3) clickUp[2] = 0;
            TrySend(hid, clickUp);
            System.Threading.Thread.Sleep(stepDelayMs);
        }

        private static void TrySend(HIDController hid, byte[] buf)
        {
            try { hid.SendData(buf, (uint)buf.Length); } catch { }
        }

        private static void TargetTest(HIDController hid, byte[] baseBuf, int kp, int settleMs)
        {
            Console.WriteLine("[RelMouseProbe] Target test: moving to center + corners.");
            Console.WriteLine("[RelMouseProbe] Args: --kp N (default 1), --settle MS (default 800)");

            var b = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            var targets = new List<POINT>
            {
                new POINT { X = b.Left + b.Width / 2, Y = b.Top + b.Height / 2 },
                new POINT { X = b.Left + 10, Y = b.Top + 10 },
                new POINT { X = b.Right - 10, Y = b.Top + 10 },
                new POINT { X = b.Right - 10, Y = b.Bottom - 10 },
                new POINT { X = b.Left + 10, Y = b.Bottom - 10 },
            };

            CenterCursor();
            System.Threading.Thread.Sleep(250);

            int i = 0;
            foreach (var t in targets)
            {
                i++;
                Console.WriteLine($"  Target {i}/{targets.Count}: ({t.X},{t.Y})");
                var res = ServoToTarget(hid, baseBuf, t, kp, settleMs);
                Console.WriteLine($"    End: ({res.End.X},{res.End.Y}) err=({res.ErrX},{res.ErrY}) steps={res.Steps} maxStep=({res.MaxDx},{res.MaxDy})");
            }
        }

        private sealed class ServoResult
        {
            public POINT End;
            public int ErrX;
            public int ErrY;
            public int Steps;
            public int MaxDx;
            public int MaxDy;
        }

        private static ServoResult ServoToTarget(HIDController hid, byte[] baseBuf, POINT target, int kp, int settleMs)
        {
            var r = new ServoResult();

            var start = Environment.TickCount;
            int steps = 0;

            // keep sending deltas until we either reach target (small error) or timeout
            while (Environment.TickCount - start < settleMs)
            {
                steps++;
                var cur = GetCursorPosSafe();
                int ex = target.X - cur.X;
                int ey = target.Y - cur.Y;

                // small deadzone
                if (Math.Abs(ex) <= 1 && Math.Abs(ey) <= 1)
                    break;

                int dx = ex * kp;
                int dy = ey * kp;

                dx = Clamp(dx, -127, 127);
                dy = Clamp(dy, -127, 127);

                if (Math.Abs(dx) > Math.Abs(r.MaxDx)) r.MaxDx = dx;
                if (Math.Abs(dy) > Math.Abs(r.MaxDy)) r.MaxDy = dy;

                var buf = (byte[])baseBuf.Clone();
                // Assume candidate layout is [rid, cmd, btn, dx, dy] for any candidate used here.
                // If the candidate is shorter, just skip.
                if (buf.Length >= 5)
                {
                    buf[3] = unchecked((byte)(sbyte)dx);
                    buf[4] = unchecked((byte)(sbyte)dy);
                    TrySend(hid, buf);
                }

                System.Threading.Thread.Sleep(8);
            }

            var end = GetCursorPosSafe();
            r.End = end;
            r.ErrX = target.X - end.X;
            r.ErrY = target.Y - end.Y;
            r.Steps = steps;
            return r;
        }

        private static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private static List<Candidate> BuildCandidates()
        {
            // Assumptions:
            // - HIDController.SendData currently calls HidD_SetFeature(handle, buffer, bufferLength + 1)
            //   so we pass only the “payload” bytes here as buf.
            // - Common layouts:
            //   [rid, buttons, dx8, dy8]
            //   [rid, cmd, buttons, dx8, dy8]
            //   [rid, buttons, dx16, dy16]
            //   [rid, cmd, buttons, dx16, dy16]

            var rids = new byte[] { 0, 1, 2, 3, 5 };
            var cmds = new byte[] { 0, 1, 2, 3, 5, 0x10, 0x20 };

            var list = new List<Candidate>();

            foreach (var rid in rids)
            {
                // 8-bit deltas
                list.Add(new Candidate { Name = $"rid={rid} [rid, btn, dx8, dy8]", Bytes = new byte[] { rid, 0, 0, 0 } });
                list.Add(new Candidate { Name = $"rid={rid} [rid, dx8, dy8, btn]", Bytes = new byte[] { rid, 0, 0, 0 } });
                list.Add(new Candidate { Name = $"rid={rid} [rid, dx8, dy8]", Bytes = new byte[] { rid, 0, 0 } });
                list.Add(new Candidate { Name = $"rid={rid} [rid, btn, dx8, dy8, wheel]", Bytes = new byte[] { rid, 0, 0, 0, 0 } });
                list.Add(new Candidate { Name = $"rid={rid} [rid, btn, dx8, dy8, wheel, pad]", Bytes = new byte[] { rid, 0, 0, 0, 0, 0 } });

                foreach (var cmd in cmds)
                {
                    list.Add(new Candidate { Name = $"rid={rid} cmd={cmd} [rid, cmd, btn, dx8, dy8]", Bytes = new byte[] { rid, cmd, 0, 0, 0 } });
                    list.Add(new Candidate { Name = $"rid={rid} cmd={cmd} [rid, cmd, dx8, dy8, btn]", Bytes = new byte[] { rid, cmd, 0, 0, 0 } });
                    list.Add(new Candidate { Name = $"rid={rid} cmd={cmd} [rid, btn, cmd, dx8, dy8]", Bytes = new byte[] { rid, 0, cmd, 0, 0 } });
                    list.Add(new Candidate { Name = $"rid={rid} cmd={cmd} [rid, cmd, btn, dx8, dy8, wheel]", Bytes = new byte[] { rid, cmd, 0, 0, 0, 0 } });
                    list.Add(new Candidate { Name = $"rid={rid} cmd={cmd} [rid, cmd, btn, dx8, dy8, wheel, pad]", Bytes = new byte[] { rid, cmd, 0, 0, 0, 0, 0 } });
                }

                // 16-bit deltas (little endian)
                list.Add(new Candidate { Name = $"rid={rid} [rid, btn, dx16, dy16]", Bytes = new byte[] { rid, 0, 0, 0, 0, 0 } });
                list.Add(new Candidate { Name = $"rid={rid} [rid, dx16, dy16, btn]", Bytes = new byte[] { rid, 0, 0, 0, 0, 0 } });
                list.Add(new Candidate { Name = $"rid={rid} [rid, btn, dx16, dy16, wheel8]", Bytes = new byte[] { rid, 0, 0, 0, 0, 0, 0 } });
                list.Add(new Candidate { Name = $"rid={rid} [rid, btn, dx16, dy16, wheel16]", Bytes = new byte[] { rid, 0, 0, 0, 0, 0, 0, 0 } });

                foreach (var cmd in cmds)
                {
                    list.Add(new Candidate { Name = $"rid={rid} cmd={cmd} [rid, cmd, btn, dx16, dy16]", Bytes = new byte[] { rid, cmd, 0, 0, 0, 0, 0 } });
                    list.Add(new Candidate { Name = $"rid={rid} cmd={cmd} [rid, cmd, dx16, dy16, btn]", Bytes = new byte[] { rid, cmd, 0, 0, 0, 0, 0 } });
                    list.Add(new Candidate { Name = $"rid={rid} cmd={cmd} [rid, btn, cmd, dx16, dy16]", Bytes = new byte[] { rid, 0, cmd, 0, 0, 0, 0 } });
                    list.Add(new Candidate { Name = $"rid={rid} cmd={cmd} [rid, cmd, btn, dx16, dy16, wheel8]", Bytes = new byte[] { rid, cmd, 0, 0, 0, 0, 0, 0 } });
                    list.Add(new Candidate { Name = $"rid={rid} cmd={cmd} [rid, cmd, btn, dx16, dy16, wheel16]", Bytes = new byte[] { rid, cmd, 0, 0, 0, 0, 0, 0, 0 } });
                }

                // Some devices include a padding/reserved byte
                list.Add(new Candidate { Name = $"rid={rid} [rid, cmd, btn, dx16, dy16, pad]", Bytes = new byte[] { rid, 2, 0, 0, 0, 0, 0, 0 } });
            }

            // De-dupe by bytes (some combos overlap)
            return list
                .GroupBy(c => BitConverter.ToString(c.Bytes))
                .Select(g => g.First())
                .ToList();
        }

        private static byte[] WithDelta(byte[] baseBuf, int dx, int dy)
        {
            var b = (byte[])baseBuf.Clone();

            // Heuristic: if it looks like a 16-bit layout (len 6/7/8), write dx/dy as int16 at the end.
            // Otherwise write dx/dy into last two bytes as int8.

            if (b.Length >= 6)
            {
                // Possible positions:
                //  len=6: [rid,btn,dxLo,dxHi,dyLo,dyHi]
                //  len=7: [rid,cmd,btn,dxLo,dxHi,dyLo,dyHi]
                //  len=8: [rid,cmd,btn,dxLo,dxHi,dyLo,dyHi,pad]

                // Default assumption: dx/dy start after [rid,btn] or [rid,cmd,btn]
                int dxPos = (b.Length == 6) ? 2 : 3;
                int dyPos = dxPos + 2;

                // Alternate layouts where buttons are at the end: [rid, dx16, dy16, btn] or [rid, cmd, dx16, dy16, btn]
                if (b.Length == 6) dxPos = 1;            // [rid, dxLo, dxHi, dyLo, dyHi, btn]
                if (b.Length == 7) dxPos = 2;            // [rid, cmd, dxLo, dxHi, dyLo, dyHi, btn]
                if (b.Length >= 8 && b.Length <= 9) dxPos = 3; // keep default for [rid, cmd, btn, ...]

                dyPos = dxPos + 2;

                short sdx = (short)dx;
                short sdy = (short)dy;

                b[dxPos + 0] = (byte)(sdx & 0xFF);
                b[dxPos + 1] = (byte)((sdx >> 8) & 0xFF);
                b[dyPos + 0] = (byte)(sdy & 0xFF);
                b[dyPos + 1] = (byte)((sdy >> 8) & 0xFF);
                return b;
            }

            // 8-bit fallback: last two bytes are dx/dy
            if (b.Length >= 2)
            {
                // Try a few plausible placements.
                // Common: ... dx, dy at the end
                b[b.Length - 2] = unchecked((byte)(sbyte)dx);
                b[b.Length - 1] = unchecked((byte)(sbyte)dy);

                // Alternate: rid, dx, dy (len=3)
                if (b.Length == 3)
                {
                    b[1] = unchecked((byte)(sbyte)dx);
                    b[2] = unchecked((byte)(sbyte)dy);
                }

                // Alternate: rid, btn, dx, dy (len=4)
                if (b.Length == 4)
                {
                    // If this candidate intended [rid, dx, dy, btn]
                    b[1] = unchecked((byte)(sbyte)dx);
                    b[2] = unchecked((byte)(sbyte)dy);
                }
            }

            return b;
        }

        private static int GetIntArg(string[] args, string key, int def)
        {
            try
            {
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                    {
                        int v;
                        if (int.TryParse(args[i + 1], out v))
                            return v;
                    }
                }
            }
            catch { }
            return def;
        }
    }
}
