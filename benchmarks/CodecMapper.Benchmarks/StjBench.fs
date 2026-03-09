namespace CodecMapper.Benchmarks

open System.Text.Json

module StjBench =
    let serialize p = JsonSerializer.Serialize(p)
    let deserialize<'T> (json: string) = JsonSerializer.Deserialize<'T>(json)
