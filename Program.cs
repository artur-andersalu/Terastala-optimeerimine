using FemDesign;
using FemDesign.Bars;
using FemDesign.Geometry;
using FemDesign.Loads;
using FemDesign.Materials;
using FemDesign.Releases;
using FemDesign.Results;
using FemDesign.Supports;
using FemDesign.Sections;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Terastala_optimeerimine
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Sisesta lihttala pikkus meetrites");
            var Lstr = Console.ReadLine();
            double L = double.TryParse(Lstr, out double parsedL) ? parsedL : 6.0;

            // Geometry
            var x1 = new Point3d(0, 0, 0);
            var x2 = new Point3d(L, 0, 0);
            var edge = new Edge(x1, x2);

            // Material
            var materialDb = MaterialDatabase.GetDefault("EST");
            var myMaterial = materialDb.MaterialByName("S 235");

            // Sections
            var sectionDb = SectionDatabase.DeserializeStruxml("../../../SectionDatabase.struxml");


            var heASections = sectionDb.Sections.Section
                .Where(s => s.Name.StartsWith("Steel sections, HE-A"))
                .ToList();

            // Supports

            var rigidV = 10000000;
            var support1 = PointSupport.Hinged(x1);
            var MXfree = Motions.Define(0,0,rigidV,rigidV,rigidV,rigidV);
            var rigid = Motions.RigidPoint();
            var rRigid = Rotations.RigidPoint();
            var Rfree=Rotations.Free();
            var support2 = new PointSupport(x2, MXfree, Rfree);

            var supports = new List<FemDesign.GenericClasses.ISupportElement> { support1, support2 };

            // Load Cases
            var loadCaseDead = new LoadCase("OK", LoadCaseType.DeadLoad, LoadCaseDuration.Permanent);
            var loadCaseVariable = new LoadCase("Variable", LoadCaseType.Static, LoadCaseDuration.Permanent);
            var loadCases = new List<LoadCase> { loadCaseDead, loadCaseVariable };

            // Loads
            double gk = 2.5; //omakaal lauskoormus kN/m2
            double qk = 5; //omakaal lauskoormus kN/m2

            //var x3 = new Point3d(L / 2, 0, 0);
            //var v1 = new Vector3d(0, 0, -10); // Point load
            //var v2 = new Vector3d(0, 0, -15);  // Line load

           // var pointLoad = new PointLoad(x3, v1, loadCaseWind, "", ForceLoadType.Force);
            //var lineLoad = new LineLoad(edge, v2, loadCaseWind, ForceLoadType.Force);
            //var loads = new List<FemDesign.GenericClasses.ILoadElement> { pointLoad, lineLoad };

            // Load Combination
            var gammas = new List<double> { 1.2, 1.5 };
            var combination = new LoadCombination("ULS", LoadCombType.UltimateOrdinary, loadCases, gammas);

            // Result tracking
            double maxAllowedDisplacement = L/250*1000; // mm
           // double minWeight = double.MaxValue;
            string bestSection = null;
            double bestRatio = double.MaxValue;

            // Setup of default units
            var units = new FemDesign.Results.UnitResults();
            units.Displacement = FemDesign.Results.Displacement.mm;

            // Define surface load and step range
           // double surfaceLoad = 10.0; // kN/m²
            double[] steps = {0.6, 0.8, 1.0, 1.2, 1.4}; // meters

            // Define list for results
            var results = new List<(string SectionName, double Step, double Ratio)>();

            foreach (double step in steps)
            {
                var lineLoadValueGk = gk * step; // kN/m for one beam
                var lineLoadValueQk = qk * step; // kN/m for one beam
                var v1 = new Vector3d(0, 0, -lineLoadValueGk);  // Updated line load
                var v2 = new Vector3d(0, 0, -lineLoadValueQk);  // Updated line load
                var lineLoad1 = new LineLoad(edge, v1, loadCaseDead, ForceLoadType.Force);
                var lineLoad2 = new LineLoad(edge, v2, loadCaseVariable, ForceLoadType.Force);
                var loads = new List<FemDesign.GenericClasses.ILoadElement> { lineLoad1,lineLoad2 };

                foreach (var section in heASections)
                {
                    var beam = new Beam(edge, myMaterial, section);
                    var model = new Model(Country.EST);

                    model.AddLoadCases(loadCaseDead, loadCaseVariable);
                    model.AddLoadCombinations(combination);
                    model.AddLoads(loads);
                    model.AddSupports(supports);
                    model.AddElements(new List<FemDesign.GenericClasses.IStructureElement> { beam });

                    using (var connection = new FemDesignConnection(keepOpen: false))
                    {
                        connection.Open(model);
                        connection.RunAnalysis(FemDesign.Calculate.Analysis.StaticAnalysis());

                        var displ = connection.GetResults<BarDisplacement>(units);
                        var maxDispl = Math.Abs(displ.Select(x => x.Ez).Min());

                        var quantities = connection.GetQuantities<QuantityEstimationSteel>();
                        var totalWeight = quantities.Select(x => x.TotalWeight).Sum();
                        var weightPerMeter = totalWeight / L;

                        var ratio = maxDispl / weightPerMeter;

                        Console.WriteLine($"Step: {step * 1000:F0}mm, Section: {section.Name}, Displ: {maxDispl:F2} mm, Weight: {weightPerMeter:F2} kg/m, Ratio: {ratio:F4}");

                        if (maxDispl <= maxAllowedDisplacement)
                        {
                            bestSection = section.Name;
                            bestRatio = ratio;
                            Console.WriteLine("✅ Criteria met, stopping early.");
                           
                            results.Add((section.Name, step, ratio));
                        
                            break;
                        }


                    }
                }
            }

            // Now after loops you can access the results
            if (results.Any())
            {
                var bestResult = results.OrderByDescending(r => r.Ratio).First();
                Console.WriteLine($"Best Section: {bestResult.SectionName}, Step: {bestResult.Step}, Ratio: {bestResult.Ratio}");
            }
            else
            {
                Console.WriteLine("No suitable results found.");
            }
            Console.ReadKey();

        }
    }

}
