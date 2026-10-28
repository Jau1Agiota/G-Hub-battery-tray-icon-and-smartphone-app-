using System.Text;
using Microsoft.Data.Sqlite;
using PeripheralBatteryCompanion.G305GHubInspector;
using Xunit;
namespace PeripheralBatteryCompanion.Tests;
public sealed class G305GHubInspectionTests
{
    private static InspectionReport Inspect(string json) => JsonInspection.Inspect(Encoding.UTF8.GetBytes(json));
    [Fact]
    public void ModelReferencesAndG915BatteryNeverImplyG305Telemetry()
    {
        var result = Inspect("""{"devices":[{"modelId":"g305","baseModelId":"G305","signature":"g305-synthetic","slotId":"g305"}],"battery/g915/percentage":{"percentage":38}}""");
        Assert.True(result.Parsed); Assert.Equal(4, result.References);
        Assert.False(result.AssociatedBattery); Assert.Null(result.Percentage); Assert.Null(result.Level);
        Assert.Contains(result.Findings!, item => item.Category == "Unattributed battery/status");
    }
    [Fact]
    public void DeviceIdentityAllowsOnlyRecognizedBatteryScopeAndApprovedLevel()
    {
        var result = Inspect("""{"devices":[{"modelId":"g305","battery":{"level":"low","percentage":42},"settings":{"percentage":100,"batteryLevel":"full"}}]}""");
        Assert.True(result.AssociatedBattery); Assert.Equal(42,result.Percentage); Assert.Equal("low",result.Level);
        Assert.Contains(result.Findings!, item=>item.Category=="Configuration field");
    }
    [Theory]
    [InlineData("{\"modelId\":\"g305\",\"batteryPercentage\":73}")]
    [InlineData("{\"battery/g305/percentage\":{\"percentage\":73}}")]
    public void DirectOrExplicitModelPathCanEvidenceACachedPercent(string json) => Assert.Equal(73,Inspect(json).Percentage);
    [Theory]
    [InlineData("{\"name\":\"g305\",\"battery\":{\"percentage\":73}}")]
    [InlineData("{\"signature\":\"g305\",\"battery\":{\"percentage\":73}}")]
    [InlineData("{\"modelId\":\"g305-proprietary-unknown\",\"battery\":{\"percentage\":73}}")]
    [InlineData("{\"modelId\":\"g305\",\"other\":{\"battery\":{\"percentage\":73}}}")]
    [InlineData("{\"profiles\":[{\"modelId\":\"g305\",\"battery\":{\"percentage\":73}}]}")]
    [InlineData("{\"modelId\":\"g305\",\"telemetry\":{\"modelId\":\"g915\",\"battery\":{\"percentage\":73}}}")]
    public void UnprovenIdentityConfigurationOrDifferentDeviceCannotProduceAValue(string json)=>Assert.Null(Inspect(json).Percentage);
    [Theory]
    [InlineData("-1")] [InlineData("101")] [InlineData("42.5")] [InlineData("\"42\"")] [InlineData("null")]
    public void InvalidPercentagesRemainUnknown(string value)
    {
        var result=Inspect("{\"modelId\":\"g305\",\"batteryPercentage\":"+value+"}");
        Assert.True(result.AssociatedBattery); Assert.Null(result.Percentage);
    }
    [Theory]
    [InlineData("critical")] [InlineData("low")] [InlineData("medium")] [InlineData("high")] [InlineData("full")] [InlineData("unknown")]
    public void QualitativeValuesAreAnApprovedEnumNotAnInventedPercentage(string level)
    {
        var result=Inspect("{\"modelId\":\"g305\",\"batteryLevel\":\""+level+"\"}");
        Assert.Equal(level,result.Level); Assert.Null(result.Percentage);
    }
    [Fact]
    public void ConflictingCandidatesAreOmittedAndUnrecognizedValuesAreNotEchoed()
    {
        var result=Inspect("""{"devices":[{"modelId":"g305","batteryPercentage":40},{"modelId":"g305","batteryPercentage":90}],"batteryLevel":"SECRET-TEXT"}""");
        Assert.True(result.ConflictingValues); Assert.Null(result.Percentage); Assert.DoesNotContain("SECRET",result.Format());
    }
    [Fact]
    public void KeysAndPathsCannotLeakNamesHashesSerialsOrAccountValues()
    {
        var result=Inspect("""{"private-user@example.com":{"deadbeef1234567890abcdef":{"battery_SERIAL-PRIVATE":{"name":"g305", "password":"SUPER-SECRET", "instance":"hid#private"}}}}""");
        var text=result.Format();
        foreach(var secret in new[]{"private-user","deadbeef","SERIAL","SUPER-SECRET","hid#private"})Assert.DoesNotContain(secret,text,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<key>",text);Assert.Contains("<battery-key>",text);
    }
    [Fact]
    public void OutputAndTraversalLimitsSuppressValuesFromPartialScans()
    {
        var fields=string.Join(',',Enumerable.Range(0,200).Select(i=>$"\"battery_private{i}\":{i}"));
        var result=Inspect("{"+fields+"}");
        Assert.True(result.Limited); Assert.True(result.Findings!.Count<=JsonInspection.MaxFindings); Assert.True(result.Format().Length<10000);
        var huge="["+string.Join(',',Enumerable.Repeat("{}",JsonInspection.MaxNodes+1))+"]";
        Assert.True(Inspect(huge).Limited);
        Assert.Equal(InspectionStatus.LimitExceeded,JsonInspection.Inspect(new byte[JsonInspection.MaxBytes+1]).Status);
    }
    [Theory]
    [InlineData("{")] [InlineData("42")] [InlineData("null")]
    public void MalformedOrNonContainerJsonIsAControlledFailure(string json)=>Assert.Equal(InspectionStatus.InvalidJson,Inspect(json).Status);
    [Fact]
    public void ExcessDepthIsAControlledFailure()
    {
        var json=new string('[',JsonInspection.MaxDepth+1)+"0"+new string(']',JsonInspection.MaxDepth+1);
        Assert.Equal(InspectionStatus.InvalidJson,Inspect(json).Status);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SyntheticSqliteReadsLatestTextOrBlobInReadOnlySnapshot(bool blob)
    {
        using var db=new Database(); db.Insert("{\"battery/g915/percentage\":{\"percentage\":38}}",false);
        db.Insert("{\"modelId\":\"g305\",\"batteryLevel\":\"medium\"}",blob);
        var before=File.ReadAllBytes(db.Path);
        var report=DatabaseInspection.Read(db.Path);
        Assert.Equal(InspectionStatus.Success,report.Status);Assert.True(report.Opened);Assert.True(report.SchemaSupported);
        Assert.True(report.AdditionalRows is true);Assert.Equal(1,report.RowsInspected);Assert.Equal("medium",report.Level);
        Assert.Equal(blob?"blob":"text",report.PayloadType);Assert.Equal(before,File.ReadAllBytes(db.Path));
    }
    [Fact]
    public void MissingDatabaseIsNotCreatedAndPrivatePathIsNotPrinted()
    {
        var path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),Guid.NewGuid()+"-private-user.db");
        var report=DatabaseInspection.Read(path);Assert.Equal(InspectionStatus.MissingOrInaccessible,report.Status);
        Assert.False(File.Exists(path));Assert.Null(report.AdditionalRows);Assert.DoesNotContain("private-user",report.Format());
    }
    [Fact]
    public void EmptyMissingTableAndMalformedPayloadStayDistinct()
    {
        using var db=new Database();Assert.Equal(InspectionStatus.Empty,DatabaseInspection.Read(db.Path).Status);
        db.Insert("{",false);Assert.Equal(InspectionStatus.InvalidJson,DatabaseInspection.Read(db.Path).Status);
        using(var c=db.Open()){using var cmd=c.CreateCommand();cmd.CommandText="DROP TABLE data";cmd.ExecuteNonQuery();}
        Assert.Equal(InspectionStatus.UnsupportedSchema,DatabaseInspection.Read(db.Path).Status);
    }
    [Fact]
    public void LockedDatabaseFailsWithoutRetryLoopOrSensitiveException()
    {
        using var db=new Database();using var writer=db.Open();using var cmd=writer.CreateCommand();cmd.CommandText="BEGIN EXCLUSIVE";cmd.ExecuteNonQuery();
        try {var report=DatabaseInspection.Read(db.Path);Assert.Equal(InspectionStatus.SqliteFailure,report.Status);Assert.Equal(5,report.SqliteCode);Assert.DoesNotContain(db.Path,report.Format());}
        finally {cmd.CommandText="ROLLBACK";cmd.ExecuteNonQuery();}
    }
    [Fact]
    public void LiveCommittedWalIsSeenWithoutTouchingTheOpenWriter()
    {
        using var db=new Database();using var writer=db.Open();using var cmd=writer.CreateCommand();cmd.CommandText="PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0";cmd.ExecuteNonQuery();
        cmd.CommandText="INSERT INTO data(file) VALUES ('{\"modelId\":\"g305\",\"batteryPercentage\":60}')";cmd.ExecuteNonQuery();
        var report=DatabaseInspection.Read(db.Path);Assert.Equal(60,report.Percentage);
        cmd.CommandText="SELECT COUNT(*) FROM data";Assert.Equal(1L,cmd.ExecuteScalar());
    }
    [Fact]
    public void OversizedSqlitePayloadIsRejectedBeforeMaterialization()
    {
        using var db=new Database();using var c=db.Open();using var cmd=c.CreateCommand();cmd.CommandText=$"INSERT INTO data(file) VALUES (zeroblob({JsonInspection.MaxBytes+1}))";cmd.ExecuteNonQuery();
        Assert.Equal(InspectionStatus.LimitExceeded,DatabaseInspection.Read(db.Path).Status);
    }
    [Fact]
    public void EntryPointCopiesOnlyFixedFailureEvenForUnexpectedPrivateException()
    {
        using var writer=new StringWriter();var code=InspectorEntry.Execute(()=>throw new IOException("private-user SECRET"),writer);
        Assert.Equal(1,code);Assert.DoesNotContain("SECRET",writer.ToString());Assert.Contains("UnexpectedFailure",writer.ToString());
    }
    [Fact]
    public void DiagnosticHasNoProductionOrHidDependenciesOrNativeImports()
    {
        var assembly=typeof(DatabaseInspection).Assembly;
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), name=>name.Name!.StartsWith("PeripheralBatteryCompanion."));
        Assert.DoesNotContain(assembly.GetTypes().SelectMany(type=>type.GetMethods(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance)),
            method=>method.GetCustomAttributes(typeof(System.Runtime.InteropServices.DllImportAttribute),false).Length!=0);
    }
    [Fact]
    public void NonSqliteAndUnsupportedColumnsAreControlledFailures()
    {
        using var db=new Database();
        using(var connection=db.Open()){using var cmd=connection.CreateCommand();cmd.CommandText="DROP TABLE data; CREATE TABLE data(other TEXT)";cmd.ExecuteNonQuery();}
        Assert.Equal(InspectionStatus.UnsupportedSchema,DatabaseInspection.Read(db.Path).Status);
        File.WriteAllText(db.Path,"not a database");
        var failure=DatabaseInspection.Read(db.Path);
        Assert.Equal(InspectionStatus.SqliteFailure,failure.Status);Assert.DoesNotContain(db.Path,failure.Format());
    }

    [Fact]
    public void PropertyNamesAndStringReferencesStayDistinctAndLiteralSlashesAreEscaped()
    {
        var result=Inspect("""{"g305":"not a model value","modelId":"g305","battery/g305/level":{"level":"low"},"low_battery_notifications_enabled":true}""");
        Assert.Contains(result.Findings!,item=>item.Category=="Model/configuration key reference" && item.Path=="$/g305");
        Assert.Contains(result.Findings!,item=>item.Category=="Model/configuration value reference" && item.Path=="$/modelid");
        Assert.Contains(result.Findings!,item=>item.Path.Contains("battery~1g305~1level"));
        Assert.Contains(result.Findings!,item=>item.Path.EndsWith("low_battery_notifications_enabled") && item.Type=="Boolean");
        Assert.DoesNotContain("[True]",result.Format());
    }

    private sealed class Database : IDisposable
    {
        private readonly string _directory=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"pbc-g305-"+Guid.NewGuid().ToString("N"));
        public string Path=>System.IO.Path.Combine(_directory,"synthetic.db");
        public Database(){Directory.CreateDirectory(_directory);using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="CREATE TABLE data(_id INTEGER PRIMARY KEY, file)";cmd.ExecuteNonQuery();}
        public SqliteConnection Open(){var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path,Pooling=false,DefaultTimeout=1}.ToString());c.Open();return c;}
        public void Insert(string json,bool blob){using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="INSERT INTO data(file) VALUES ($value)";cmd.Parameters.AddWithValue("$value",blob?Encoding.UTF8.GetBytes(json):(object)json);cmd.ExecuteNonQuery();}
        public void Dispose()=>Directory.Delete(_directory,true);
    }
}
