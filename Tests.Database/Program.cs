using System;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Database
{
    /// <summary>
    /// Runs the [TestMethod]s in this assembly without a test runner.
    ///
    /// Mono has no working vstest.console, so on Linux this is how the suite runs; on
    /// Windows the same methods are discovered by Visual Studio and by the CI runner in
    /// .github/workflows/test_pullrequest.yml. There is one set of tests either way.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            // Not a test run: produce the rating table for the cross-stack comparison.
            if (args.FirstOrDefault() == "--dump-ratings") return RatingDump.Run(args.Skip(1).FirstOrDefault());

            var filter = args.FirstOrDefault();
            int passed = 0, failed = 0, skipped = 0;

            foreach (var type in Assembly.GetExecutingAssembly().GetTypes()
                         .Where(t => t.GetCustomAttribute<TestClassAttribute>() != null)
                         .OrderBy(t => t.Name))
            {
                var methods = type.GetMethods()
                    .Where(m => m.GetCustomAttribute<TestMethodAttribute>() != null)
                    .Where(m => filter == null || m.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(m => m.Name)
                    .ToList();
                if (!methods.Any()) continue;

                Console.WriteLine();
                Console.WriteLine(type.Name);

                object instance;
                try
                {
                    instance = Activator.CreateInstance(type);
                    var init = type.GetMethods().FirstOrDefault(m => m.GetCustomAttribute<TestInitializeAttribute>() != null);
                    if (init != null) init.Invoke(instance, null);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  ! could not set up: " + Unwrap(ex).Message);
                    failed += methods.Count;
                    continue;
                }

                foreach (var m in methods)
                {
                    try
                    {
                        m.Invoke(instance, null);
                        Console.WriteLine("  pass  " + m.Name);
                        passed++;
                    }
                    catch (Exception ex)
                    {
                        var real = Unwrap(ex);
                        if (real is AssertInconclusiveException)
                        {
                            Console.WriteLine("  skip  " + m.Name + " - " + real.Message);
                            skipped++;
                        }
                        else
                        {
                            Console.WriteLine("  FAIL  " + m.Name);
                            Console.WriteLine("        " + real.Message.Replace("\n", "\n        "));
                            failed++;
                        }
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine(string.Format("{0} passed, {1} failed, {2} skipped", passed, failed, skipped));
            return failed == 0 ? 0 : 1;
        }

        static Exception Unwrap(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            return ex;
        }
    }
}
