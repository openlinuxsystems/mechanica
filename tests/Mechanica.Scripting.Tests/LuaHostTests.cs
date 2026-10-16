using MoonSharp.Interpreter;

namespace Mechanica.Scripting.Tests;

public class LuaHostTests
{
    [Fact]
    public void RunsLuaAndReturnsResult()
    {
        var host = new LuaHost();
        Assert.Equal(42.0, host.Run("return 40 + 2"));
    }

    [Fact]
    public void CallsGlobalFunction()
    {
        var host = new LuaHost();
        host.Run("function on_power(amount) return amount * 2 end");
        Assert.Equal(10.0, host.Call("on_power", 5));
    }

    [Theory]
    [InlineData("return io.open('x')")]
    [InlineData("return os.execute('true')")]
    [InlineData("return dofile('x.lua')")]
    public void SandboxBlocksFileAndProcessAccess(string source)
    {
        var host = new LuaHost();
        Assert.ThrowsAny<InterpreterException>(() => host.Run(source));
    }
}
