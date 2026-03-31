namespace CodecMapper

/// Represents arbitrary JSON when a contract cannot be lowered into a more precise contract.
///
/// This is the escape hatch for dynamic-key objects, heterogeneous arrays, and
/// other JSON Schema shapes that do not fit the normal record/list/primitive
/// model without losing parseability.
type JsonValue =
    | JNull
    | JBool of bool
    | JNumber of string
    | JString of string
    | JArray of JsonValue list
    | JObject of (string * JsonValue) list
