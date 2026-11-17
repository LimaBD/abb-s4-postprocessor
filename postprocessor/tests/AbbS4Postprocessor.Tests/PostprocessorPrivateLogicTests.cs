using System.Reflection;
using System.Collections;
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

        Assert.Equal(@"D:\output\PART_A_MASTER.PRG", path);
    }

    [Fact]
    public void BuildRapidModuleText_UsesExercisePrgStyleInlineSyntax()
    {
        var sut = new Postprocessor();
        SetField(sut, "baseProgramName", "EXERCISE");
        SetField(sut, "zoneName", "fine");
        SetField(sut, "toolName", "tool0");

        Type pointType = typeof(Postprocessor).GetNestedType("MotionPoint", BindingFlags.NonPublic)!;
        object point = Activator.CreateInstance(pointType)!;
        SetProperty(point, "IndexInChunk", 1);
        SetProperty(point, "X", 809d);
        SetProperty(point, "Y", -384.3d);
        SetProperty(point, "Z", 1663.9d);
        SetProperty(point, "Q1", 0.36169d);
        SetProperty(point, "Q2", 0.04361d);
        SetProperty(point, "Q3", 0.93128d);
        SetProperty(point, "Q4", -0.0011d);
        SetProperty(point, "SpeedMmPerSec", 300);
        SetProperty(point, "JointMotion", false);
        SetProperty(point, "CommentsBeforeMove", new List<string>());

        var points = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(pointType))!;
        points.Add(point);

        string text = InvokeStringInstanceMethod(sut, "BuildRapidModuleText", points, 0);

        Assert.Contains("%%%", text);
        Assert.Contains("VERSION:1", text);
        Assert.Contains("LANGUAGE:ENGLISH", text);
        Assert.Contains("MODULE EXERCISE", text);
        Assert.Contains("PERS wobjdata wobj_table := [FALSE,TRUE,\"\",[[0,0,0],[1,0,0,0]],[[0,0,0],[1,0,0,0]]];", text);
        Assert.Contains("VAR speeddata speed300:=[300,500,5000,1000];", text);
        Assert.Contains("MoveL [[809,-384.3,1663.9],[0.36169,0.04361,0.93128,-0.0011],[0,0,0,0],[9E+09,9E+09,9E+09,9E+09,9E+09,9E+09]],speed300,FINE,TOOL0\\WObj:=wobj_table;", text);
    }

    [Fact]
    public void ResolveSpeedMmPerSec_BucketsByFiveMillimetersPerSecond()
    {
        var sut = new Postprocessor();

        SetField(sut, "currentFeedMmPerMin", 67d * 60d);
        int first = InvokeIntInstanceMethod(sut, "ResolveSpeedMmPerSec", false);

        SetField(sut, "currentFeedMmPerMin", 63d * 60d);
        int second = InvokeIntInstanceMethod(sut, "ResolveSpeedMmPerSec", false);

        Assert.Equal(65, first);
        Assert.Equal(65, second);
    }

    private static void SetProperty(object target, string propertyName, object value)
    {
        PropertyInfo? property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        property.SetValue(target, value);
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

    private static int InvokeIntInstanceMethod(object target, string methodName, params object[] args)
    {
        MethodInfo? method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        object? result = method.Invoke(target, args);
        Assert.NotNull(result);
        return Assert.IsType<int>(result);
    }
}
