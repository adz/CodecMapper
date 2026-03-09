namespace cmap.Benchmarks

open Newtonsoft.Json

module NewtonsoftBench =
    let serialize p = JsonConvert.SerializeObject(p)
    let deserialize<'T> (json: string) = JsonConvert.DeserializeObject<'T>(json)
