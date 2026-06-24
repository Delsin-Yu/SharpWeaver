using SharpWeaver.Examples.AsyncTaskWeave;
using SharpWeaver.Examples.EarlyReturnSkip;
using SharpWeaver.Examples.ExceptionWrap;
using SharpWeaver.Examples.GenericWeaveCapture;
using SharpWeaver.Examples.MultiTargetWeave;
using SharpWeaver.Examples.PrefixPostfixLogging;
using SharpWeaver.Examples.PriorityLayering;
using SharpWeaver.Examples.StaticMethodWeave;
using SharpWeaver.Examples.VirtualOverrideWeave;
using SharpWeaver.Examples.WeaveExclude;
using SharpWeaver.Examples.WildcardInstrumentation;

Console.WriteLine("=== SharpWeaver.Examples ===");
Console.WriteLine("(Build in Debug so SharpWeaver runs after compile.)");
Console.WriteLine();

new Greeter().SayHello("world");

try
{
    _ = new Calculator().Divide(10, 0);
}
catch (DivideByZeroException)
{
    // Expected after woven exception logging.
}

CounterWeavePatch.CachedValue = 99;
Console.WriteLine($"GetNext (cached): {new Counter().GetNext()}");
CounterWeavePatch.CachedValue = null;
Console.WriteLine($"GetNext (live): {new Counter().GetNext()}");

new OrderService().PlaceOrder(1);
new PaymentService().Charge(500);

new IncludedTarget().Included();
new ExcludedTarget().Excluded();

new AlphaService().Run();
new BetaService().Run();

new LayeredService().Execute();

Console.WriteLine($"Static Add: {MathUtil.Add(2, 3)}");

new WorkerDerived().DoWork(5);

await new AsyncWorker().RunAsync();

Console.WriteLine($"Generic Echo: {new GenericBox().Echo("hi")}");
Console.WriteLine($"Generic Echo: {new GenericBox().Echo(42)}");
