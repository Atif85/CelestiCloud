using System.Text.Json.Serialization;
using CelestiCloud.Core.Models;

namespace CelestiCloud.Core.Config;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(JobConfig))]
[JsonSerializable(typeof(AccountConfig))]
[JsonSerializable(typeof(AppSettings))]
internal partial class CelestiJsonContext : JsonSerializerContext
{
}