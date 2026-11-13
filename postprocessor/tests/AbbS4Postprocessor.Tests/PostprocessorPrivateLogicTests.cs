using System.Reflection;
using SprutTechnology.SCPostprocessor;
using Xunit;

namespace AbbS4Postprocessor.Tests;

public class PostprocessorPrivateLogicTests
{
    [Fact]
    public void BuildModuleName_UsesSuffixForSplitChunks()
    {
        var sut = new Postprocessor();
        SetField(sut, "baseProgramName", "PART");

        string first = InvokeStringInstanceMethod(sut, "BuildModuleName", 0);
        string second = InvokeStringInstanceMethod(sut, "BuildModuleName", 1);
        string tenth = InvokeStringInstanceMethod(sut, "BuildModuleName", 9);

        Assert.Equal("PART", first);
        Assert.Equal("PART_002", second);
        Assert.Equal("PART_010", tenth);
    }

    [Fact]
    public void BuildMasterModuleText_CallsChunksInOrder()
    {
        var sut = new Postprocessor();
        SetField(sut, "baseProgramName", "DEMO");

        string text = InvokeStringInstanceMethod(
            sut,
            "BuildMasterModuleText",
            new List<string> { "DEMO", "DEMO_002", "DEMO_003" });

        Assert.Contains("MODULE DEMO_MASTER", text);
        Assert.Contains("PROC main()", text);
        Assert.Contains("DEMO\\main;", text);
        Assert.Contains("DEMO_002\\main;", text);
        Assert.Contains("DEMO_003\\main;", text);
        Assert.Contains("ENDMODULE", text);
    }

    [Fact]
    public void BuildMasterModuleName_TruncatesToRapidIdentifierLimit()
    {
        var sut = new Postprocessor();
        SetField(sut, "baseProgramName", "ABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890");

        string masterName = InvokeStringInstanceMethod(sut, "BuildMasterModuleName");

        Assert.Equal(32, masterName.Length);
        Assert.Equal("ABCDEFGHIJKLMNOPQRSTUVWXYZ123456", masterName);
    }

    [Fact]
    public void BuildMasterOutputFileName_UsesBaseNameAndOutputFolder()
    {
        var sut = new Postprocessor();
        SetField(sut, "baseProgramName", "PART_A");
        SetField(sut, "outputFolder", @"D:\output");

        string path = InvokeStringInstanceMethod(sut, "BuildMasterOutputFileName");

        Assert.Equal(@"D:\output\PART_A_MASTER.mod", path);
    }

    private static void SetField(object target, string fieldName, object value)
    {
        FieldInfo? field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(target, value);
    }

    private static string InvokeStringInstanceMethod(object target, string methodName, params object[] args)
    {
        MethodInfo? method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        object? result = method.Invoke(target, args);
        Assert.NotNull(result);
        return Assert.IsType<string>(result);
    }
}
