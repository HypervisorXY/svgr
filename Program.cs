using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using Svg;

namespace svgr
{
    class Program
    {
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
            // Draw the curve.
            var points = new List<PointF>();
            for (var t = 0.0f; t < 1.0; t += dt)
            {
                points.Add(new PointF(
                    X(t, pt0.X, pt1.X, pt2.X, pt3.X),
                    Y(t, pt0.Y, pt1.Y, pt2.Y, pt3.Y)));
            }

            // Connect to the final point.
            points.Add(new PointF(
                X(1.0f, pt0.X, pt1.X, pt2.X, pt3.X),
                Y(1.0f, pt0.Y, pt1.Y, pt2.Y, pt3.Y)));
            
            return points;
        }

        static void Main(string[] args)
        {
            Console.WriteLine();
            Console.WriteLine("svgr (Scalable Vector Graphics optimized for Rustangelo) v1.0");
            Console.WriteLine("usage: svgr [input-file (*.svg)] [output-file (*.svgr)]");
            Console.WriteLine();

            if (args.Length >= 2)
            {
                string inFile = args[0];
                string outFile = args[1];

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

                    int prg = 0;
                    int pprg = prg;

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

                    Func<PointF, PointF> transform = (p) =>
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

                    Console.WriteLine("Processing SVG: 0%");

                    var pp = PointF.Empty;

                    for (var i = 0; i < svgDoc.Path.PathPoints.Length; i++)
                    {
                        var p = svgDoc.Path.PathPoints[i];
                        var t = svgDoc.Path.PathTypes[i];

                        if (t == 0) // start
                        {
                            pp = p;
                        }
                        else if (pp != PointF.Empty && (t == 1 || t == 129 || t == 160)) // line
                        {
                            output.AppendFormat("{0},{1},{2},{3};", transform(pp).X, transform(pp).Y, transform(p).X, transform(p).Y);
                            pp = p;
                        }
                        else if (pp != PointF.Empty && (t == 3 || t == 131)) // cubic Bézier spline
                        {
                            var points = DrawBezier(0.1f, pp, p, svgDoc.Path.PathPoints[i + 1], svgDoc.Path.PathPoints[i + 2]);

                            i += 2;
                            pp = svgDoc.Path.PathPoints[i];

                            if (points.Count > 1)
                            {
                                for (var z = 0; z < points.Count - 1; z++)
                                {
                                    output.AppendFormat("{0},{1},{2},{3};", transform(points[z]).X, transform(points[z]).Y, transform(points[z + 1]).X, transform(points[z + 1]).Y);
                                }
                            }
                        }
                        else
                        {
                            pp = p;
                        }

                        prg = (int)Math.Round(((double)(i + 1) / svgDoc.Path.PathPoints.Length) * 100);
                        if (prg != pprg) Console.WriteLine(string.Format("Processing SVG: {0}%", prg));

                        pprg = prg;
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
