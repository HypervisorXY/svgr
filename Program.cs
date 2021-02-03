using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using Svg;

namespace svgr
{
    class Program
    {
        static int prg = 0;
        static int pprg = prg;
        static int progress = 0;
        static int total = 0;

        static Func<PointF, PointF> transform;
        static Action<int, int, PointF[], byte[], StringBuilder, bool> process;

        const int MAX_THREADS = 20;

        class Worker
        {
            internal String id = "";
            internal int start = 0;
            internal int stop = 0;
            internal PointF[] points;
            internal byte[] types;
            internal StringBuilder output;
            Thread thread = null;

            internal bool Ready()
            {
                return thread == null;
            }

            internal Worker Start()
            {
                thread = new Thread(_worker);
                thread.IsBackground = true;
                thread.Start();
                return this;
            }

            void _worker()
            {
                var sb = new StringBuilder();

                process(start, stop, points, types, sb, false);

                lock (output)
                {
                    output.Replace(id, sb.ToString());
                }

                progress += stop - start;

                prg = (int)Math.Round(((double)(progress + 1) / total) * 100);
                if (prg != pprg) Console.WriteLine(string.Format("Processing SVG: {0}%", prg));

                pprg = prg;
                thread = null;
            }
        }

        private static float X(float t, float x0, float x1, float x2, float x3)
        {
            return (float)(
                x0 * Math.Pow((1 - t), 3) +
                x1 * 3 * t * Math.Pow((1 - t), 2) +
                x2 * 3 * Math.Pow(t, 2) * (1 - t) +
                x3 * Math.Pow(t, 3)
            );
        }

        private static float Y(float t, float y0, float y1, float y2, float y3)
        {
            return (float)(
                y0 * Math.Pow((1 - t), 3) +
                y1 * 3 * t * Math.Pow((1 - t), 2) +
                y2 * 3 * Math.Pow(t, 2) * (1 - t) +
                y3 * Math.Pow(t, 3)
            );
        }

        private static List<PointF> DrawBezier(float dt, PointF pt0, PointF pt1, PointF pt2, PointF pt3)
        {
            var points = new List<PointF>();
            for (var t = 0.0f; t < 1.0; t += dt)
            {
                points.Add(new PointF(
                    X(t, pt0.X, pt1.X, pt2.X, pt3.X),
                    Y(t, pt0.Y, pt1.Y, pt2.Y, pt3.Y)));
            }
            
            points.Add(new PointF(
                X(1.0f, pt0.X, pt1.X, pt2.X, pt3.X),
                Y(1.0f, pt0.Y, pt1.Y, pt2.Y, pt3.Y)));
            
            return points;
        }

        static void Main(string[] args)
        {
            Console.WriteLine();
            Console.WriteLine("svgr (Scalable Vector Graphics optimized for Rustangelo) v1.1");
            Console.WriteLine("usage: svgr [input-file (*.svg)] [output-file (*.svgr)]");
            Console.WriteLine("            [/threaded   -> use multi-threading for processing]");
            Console.WriteLine();

            if (args.Length >= 2)
            {
                var inFile = args[0];
                var outFile = args[1];

                var cmdArgs = new List<string>(args);
                var threads = new List<Worker>();
                var threaded = cmdArgs.Contains("/threaded") || cmdArgs.Contains("-threaded");

                try
                {
                    if (!File.Exists(inFile))
                    {
                        throw new Exception("Input file not found: " + inFile);
                    }
                    else if (string.IsNullOrWhiteSpace(outFile))
                    {
                        throw new Exception("Invalid output file: " + outFile);
                    }

                    var watch = new Stopwatch();
                    watch.Start();

                    Console.WriteLine("Reading input file: " + inFile);

                    var svgDoc = SvgDocument.Open(inFile);
                    var output = new StringBuilder();

                    var img = svgDoc.Draw();
                    img.Dispose();

                    output.AppendFormat("Rustangelo SVG file ( svgr.rustangelo.com )|{0}x{1}|", svgDoc.Width.Value, svgDoc.Height.Value);

                    var tt = false;
                    var tx = 0f;
                    var ty = 0f;

                    var ts = false;
                    var sw = 0f;
                    var sh = 0f;

                    if (svgDoc.Children.Count > 0)
                    {
                        foreach (var c in svgDoc.Children)
                        {
                            if (c is SvgGroup && c.Transforms != null)
                            {
                                foreach (var t in c.Transforms)
                                {
                                    if (t is Svg.Transforms.SvgTranslate)
                                    {
                                        tt = true;
                                        tx = ((Svg.Transforms.SvgTranslate)t).X;
                                        ty = ((Svg.Transforms.SvgTranslate)t).Y;
                                    }
                                    else if (t is Svg.Transforms.SvgScale)
                                    {
                                        ts = true;
                                        sw = ((Svg.Transforms.SvgScale)t).X;
                                        sh = ((Svg.Transforms.SvgScale)t).Y;
                                    }
                                }

                                break;
                            }
                        }
                    }

                    transform = (p) =>
                    {
                        if (ts)
                        {
                            p.X *= sw;
                            p.Y *= sh;
                        }
                        if (tt)
                        {
                            p.X += tx;
                            p.Y += ty;
                        }

                        return p;
                    };

                    process = (start, stop, points, types, sb, prog) =>
                    {
                        var pp = (start > 0) ? points[start - 1] : PointF.Empty;

                        for (var i = start; i <= stop; i++)
                        {
                            var p = points[i];
                            var t = types[i];

                            if (t == 0) // start
                            {
                                pp = p;
                            }
                            else if (pp != PointF.Empty && (t == 1 || t == 129 || t == 160)) // line
                            {
                                sb.AppendFormat("{0},{1},{2},{3};", transform(pp).X, transform(pp).Y, transform(p).X, transform(p).Y);
                                pp = p;
                            }
                            else if (pp != PointF.Empty && (t == 3 || t == 131)) // cubic Bézier spline
                            {
                                var _points = DrawBezier(0.1f, pp, p, points[i + 1], points[i + 2]);
                                if (_points.Count > 1)
                                {
                                    for (var z = 0; z < _points.Count - 1; z++)
                                    {
                                        sb.AppendFormat("{0},{1},{2},{3};", transform(_points[z]).X, transform(_points[z]).Y, transform(_points[z + 1]).X, transform(_points[z + 1]).Y);
                                    }
                                }

                                i += 2;
                                pp = points[i];
                            }
                            else
                            {
                                pp = p;
                            }

                            if (prog)
                            {
                                prg = (int)Math.Round(((double)(i + 1) / total) * 100);
                                if (prg != pprg) Console.WriteLine(string.Format("Processing SVG: {0}%", prg));
                            }

                            pprg = prg;
                        }
                    };

                    // copy the svg data
                    var svgPoints = new List<PointF>(svgDoc.Path.PathPoints).ToArray();
                    var svgTypes = new List<byte>(svgDoc.Path.PathTypes).ToArray();

                    total = svgPoints.Length;

                    Console.WriteLine("Processing SVG: 0%");

                    if (threaded)
                    {
                        var i = 0;
                        var cnt = MAX_THREADS;
                        var size = (int)Math.Round((double)total / cnt);
                        
                        while (i < total)
                        {
                            var id = "P" + i.ToString() + ";";
                            var stop = total - 1;

                            for (var z = i + size; z < total; z++)
                            {
                                if (svgTypes[z] == 0 || svgTypes[z] == 1)
                                {
                                    stop = z - 1;
                                    break;
                                }
                            }
                            
                            lock (output)
                            {
                                output.Append(id);
                            }

                            var p = new Worker() { id = id, start = i, stop = stop, points = svgPoints, types = svgTypes, output = output };
                            threads.Add(p.Start());

                            i = stop + 1;
                        }
                    }
                    else
                    {
                        process(0, total - 1, svgPoints, svgTypes, output, true);
                    }

                    while (threads.Count > 0)
                    {
                        for (var i = threads.Count - 1; i >= 0; i--)
                        {
                            if (threads[i].Ready())
                            {
                                threads.RemoveAt(i);
                            }
                        }

                        if (threads.Count > 0) Thread.Sleep(10);
                    }

                    File.WriteAllText(outFile, output.ToString());

                    watch.Stop();

                    Console.WriteLine();
                    Console.WriteLine(String.Format("Processing completed in {0} ms.", watch.ElapsedMilliseconds));
                }
                catch (Exception ex)
                {
                    Console.WriteLine();
                    Console.WriteLine("Error: " + ex.Message);
                }
            }
        }
    }
}
