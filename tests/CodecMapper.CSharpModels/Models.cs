using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text.Json;
using CodecMapper;
using CodecMapper.Bridge;
namespace CodecMapper.CSharpModels;

public sealed class StjUppercaseStringConverter : System.Text.Json.Serialization.JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() ?? "";

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToUpperInvariant());
}

public sealed class NewtonsoftUppercaseStringConverter : Newtonsoft.Json.JsonConverter<string>
{
    public override string? ReadJson(
        Newtonsoft.Json.JsonReader reader,
        Type objectType,
        string? existingValue,
        bool hasExistingValue,
        Newtonsoft.Json.JsonSerializer serializer) =>
        reader.Value?.ToString();

    public override void WriteJson(
        Newtonsoft.Json.JsonWriter writer,
        string? value,
        Newtonsoft.Json.JsonSerializer serializer) =>
        writer.WriteValue(value?.ToUpperInvariant());
}

public sealed class StjAddress
{
    [System.Text.Json.Serialization.JsonConstructor]
    public StjAddress(string city, string postCode)
    {
        City = city;
        PostCode = postCode;
    }

    [System.Text.Json.Serialization.JsonPropertyName("city")]
    public string City { get; }

    [System.Text.Json.Serialization.JsonPropertyName("post_code")]
    public string PostCode { get; }
}

public sealed class StjUser
{
    [System.Text.Json.Serialization.JsonConstructor]
    public StjUser(int id, string displayName, StjAddress home, string[] tags, int? age)
    {
        Id = id;
        DisplayName = displayName;
        Home = home;
        Tags = tags;
        Age = age;
    }

    [System.Text.Json.Serialization.JsonPropertyName("user_id")]
    public int Id { get; }

    [System.Text.Json.Serialization.JsonPropertyName("display_name")]
    public string DisplayName { get; }

    [System.Text.Json.Serialization.JsonPropertyName("home")]
    public StjAddress Home { get; }

    [System.Text.Json.Serialization.JsonPropertyName("tags")]
    public string[] Tags { get; }

    [System.Text.Json.Serialization.JsonPropertyName("age")]
    public int? Age { get; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string InternalCode => "hidden";
}

public sealed class StjSettings
{
    [System.Text.Json.Serialization.JsonPropertyName("retry_count")]
    public int RetryCount { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("labels")]
    public List<string> Labels { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    public string InternalNote { get; set; } = "";
}

public sealed class StjCollectionSettings
{
    [System.Text.Json.Serialization.JsonConstructor]
    public StjCollectionSettings(IReadOnlyList<string> names, ICollection<int> scores)
    {
        Names = names;
        Scores = scores;
    }

    [System.Text.Json.Serialization.JsonPropertyName("names")]
    public IReadOnlyList<string> Names { get; }

    [System.Text.Json.Serialization.JsonPropertyName("scores")]
    public ICollection<int> Scores { get; }
}

public enum StjStatus
{
    Pending = 0,
    Active = 1,
    Suspended = 2
}

public sealed class StjEnumSettings
{
    [System.Text.Json.Serialization.JsonConstructor]
    public StjEnumSettings(StjStatus status)
    {
        Status = status;
    }

    [System.Text.Json.Serialization.JsonPropertyName("status")]
    public StjStatus Status { get; }
}

public sealed class StjUnsupportedConverter
{
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    [System.Text.Json.Serialization.JsonConverter(typeof(StjUppercaseStringConverter))]
    public string Name { get; set; } = "";
}

public sealed class StjUnsupportedExtensionData
{
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = new();
}

[System.Text.Json.Serialization.JsonPolymorphic]
[System.Text.Json.Serialization.JsonDerivedType(typeof(StjDerivedAnimal), "derived")]
public abstract class StjAnimal
{
    [System.Text.Json.Serialization.JsonPropertyName("kind")]
    public string Kind { get; set; } = "";
}

public sealed class StjDerivedAnimal : StjAnimal
{
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

public sealed class StjMixedBinding
{
    [System.Text.Json.Serialization.JsonConstructor]
    public StjMixedBinding(int id)
    {
        Id = id;
    }

    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public int Id { get; }

    [System.Text.Json.Serialization.JsonPropertyName("label")]
    public string Label { get; set; } = "";
}

public sealed class NewtonsoftAddress
{
    [Newtonsoft.Json.JsonConstructor]
    public NewtonsoftAddress(string city, string postCode)
    {
        City = city;
        PostCode = postCode;
    }

    [Newtonsoft.Json.JsonProperty("city")]
    public string City { get; }

    [Newtonsoft.Json.JsonProperty("post_code")]
    public string PostCode { get; }
}

public sealed class NewtonsoftUser
{
    [Newtonsoft.Json.JsonConstructor]
    public NewtonsoftUser(int id, string displayName, NewtonsoftAddress home, List<string> labels)
    {
        Id = id;
        DisplayName = displayName;
        Home = home;
        Labels = labels;
    }

    [Newtonsoft.Json.JsonProperty("user_id")]
    public int Id { get; }

    [Newtonsoft.Json.JsonProperty("display_name")]
    public string DisplayName { get; }

    [Newtonsoft.Json.JsonProperty("home")]
    public NewtonsoftAddress Home { get; }

    [Newtonsoft.Json.JsonProperty("labels")]
    public List<string> Labels { get; }

    [Newtonsoft.Json.JsonIgnore]
    public string InternalCode => "secret";
}

public sealed class NewtonsoftUnsupportedConverter
{
    [Newtonsoft.Json.JsonProperty("name")]
    [Newtonsoft.Json.JsonConverter(typeof(NewtonsoftUppercaseStringConverter))]
    public string Name { get; set; } = "";
}

public sealed class NewtonsoftUnsupportedExtensionData
{
    [Newtonsoft.Json.JsonProperty("name")]
    public string Name { get; set; } = "";

    [Newtonsoft.Json.JsonExtensionData]
    public IDictionary<string, Newtonsoft.Json.Linq.JToken> Extra { get; set; } =
        new Dictionary<string, Newtonsoft.Json.Linq.JToken>();
}

[DataContract]
public sealed class DataContractAddress
{
    public DataContractAddress(string city, string postCode)
    {
        City = city;
        PostCode = postCode;
    }

    [DataMember(Name = "city", IsRequired = true)]
    public string City { get; }

    [DataMember(Name = "post_code", IsRequired = true)]
    public string PostCode { get; }
}

[DataContract]
public sealed class DataContractUser
{
    public DataContractUser(int id, string displayName, DataContractAddress home)
    {
        Id = id;
        DisplayName = displayName;
        Home = home;
    }

    [DataMember(Name = "user_id", IsRequired = true)]
    public int Id { get; }

    [DataMember(Name = "display_name", IsRequired = true)]
    public string DisplayName { get; }

    [DataMember(Name = "home", IsRequired = true)]
    public DataContractAddress Home { get; }

    public string InternalCode => "not-on-wire";
}

[DataContract]
public sealed class DataContractSettings
{
    [DataMember(Name = "enabled", IsRequired = true)]
    public bool Enabled { get; set; }

    [DataMember(Name = "labels", IsRequired = true)]
    public List<string> Labels { get; set; } = new();

    public string InternalNote { get; set; } = "";
}

[DataContract]
[KnownType(typeof(DataContractAnimalDog))]
public abstract class DataContractAnimal
{
    [DataMember(Name = "kind", IsRequired = true)]
    public string Kind { get; set; } = "";
}

[DataContract]
public sealed class DataContractAnimalDog : DataContractAnimal
{
    [DataMember(Name = "name", IsRequired = true)]
    public string Name { get; set; } = "";
}

public sealed class FluentAddress
{
    public string Street { get; set; } = "";

    public string City { get; set; } = "";
}

public sealed class FluentUser
{
    public int Id { get; set; }

    public string DisplayName { get; set; } = "";

    public FluentAddress Home { get; set; } = new();
}

public static class FluentSchemas
{
    public static Schema<FluentAddress> Address { get; } =
        CSharpSchema.Record(() => new FluentAddress())
            .Field("street", value => value.Street, (value, field) => value.Street = field)
            .Field("city", value => value.City, (value, field) => value.City = field)
            .Build();

    public static Schema<FluentUser> User { get; } =
        CSharpSchema.Record(() => new FluentUser())
            .Field("id", value => value.Id, (value, field) => value.Id = field)
            .Field("display_name", value => value.DisplayName, (value, field) => value.DisplayName = field)
            .FieldWith("home", value => value.Home, (value, field) => value.Home = field, Address)
            .Build();
}
