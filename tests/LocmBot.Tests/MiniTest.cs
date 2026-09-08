using System;
using System.Linq;
using System.Reflection;

namespace Locm.Tests
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TestAttribute : Attribute { }

    public sealed class AssertFailed : Exception
    {
        public AssertFailed(string msg) : base(msg) { }
    }

    public static class Assert
    {
        public static void True(bool cond, string msg = null)
        {
            if (!cond) throw new AssertFailed(msg ?? "expected true");
        }

        public static void False(bool cond, string msg = null)
        {
            if (cond) throw new AssertFailed(msg ?? "expected false");
        }

        public static void Equal<T>(T expected, T actual, string msg = null)
        {
            if (!Equals(expected, actual))
                throw new AssertFailed((msg != null ? msg + ": " : "") + $"expected <{expected}>, got <{actual}>");
        }

        public static TEx Throws<TEx>(Action a) where TEx : Exception
        {
            try { a(); }
            catch (TEx e) { return e; }
            catch (Exception e) { throw new AssertFailed($"expected {typeof(TEx).Name}, got {e.GetType().Name}: {e.Message}"); }
            throw new AssertFailed($"expected {typeof(TEx).Name}, nothing thrown");
        }
    }

    public static class Runner
    {
        /// <summary>Запускает все public static void методы с [Test]. Возвращает код выхода (0 = всё зелёное).</summary>
        public static int RunAll(Assembly asm, string filter = null)
        {
            int passed = 0, failed = 0;
            var methods = asm.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Where(m => m.GetCustomAttribute<TestAttribute>() != null)
                .OrderBy(m => m.DeclaringType.Name).ThenBy(m => m.Name);

            foreach (var m in methods)
            {
                string name = m.DeclaringType.Name + "." + m.Name;
                if (filter != null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    m.Invoke(null, null);
                    passed++;
                    Console.WriteLine("  ok   " + name);
                }
                catch (TargetInvocationException tie)
                {
                    failed++;
                    var e = tie.InnerException ?? tie;
                    Console.WriteLine("  FAIL " + name + " — " + e.Message);
                    if (!(e is AssertFailed)) Console.WriteLine(e.StackTrace);
                }
            }

            Console.WriteLine($"\n{passed} passed, {failed} failed");
            return failed == 0 ? 0 : 1;
        }
    }
}
