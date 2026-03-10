namespace CodecMapper.AotTests

open CodecMapper.CompatibilitySentinel

module Program =
    [<EntryPoint>]
    let main _ = Sentinel.run "AOT"
