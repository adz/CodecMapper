using System.Collections.Generic;
using System.Runtime.Serialization;
namespace CodecMapper.CSharpModels;

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
