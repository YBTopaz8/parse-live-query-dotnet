using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Parse.Abstractions.Infrastructure;
using Parse.Abstractions.Infrastructure.Control;
using Parse.Infrastructure.Data;

namespace Parse.Infrastructure.Control;

public class ParseSetOperation : IParseFieldOperation
{
    public ParseSetOperation(object value)
    {
        Value = value;
    }

    // Replace Encode with ConvertToJSON
    public IDictionary<string, object> ConvertToJSON(IServiceHub serviceHub = default)
    {
        if (serviceHub == null)
        {
            throw new InvalidOperationException("ServiceHub is required to encode the value.");
        }

        var encodedValue = PointerOrLocalIdEncoder.Instance.Encode(Value, serviceHub);

        // For simple values, return them directly (avoid unnecessary __op)
        if (Value != null && (Value.GetType().IsPrimitive || Value is string))
        {
            return new Dictionary<string, object> { ["value"] = Value};
        }

        // If the encoded value is a dictionary, return it directly
        if (encodedValue is IDictionary<string, object> dictionary)
        {
            return dictionary;
        }

        // Default behavior for unsupported types
        throw new ArgumentException($"Unsupported type for encoding: {Value?.GetType()?.FullName}");
    }

    public IParseFieldOperation MergeWithPrevious(IParseFieldOperation previous)
    {
        // Set operation always overrides previous operations
        return this;
    }

    public object Apply(object oldValue, string key)
    {
        // Set operation always sets the field to the specified value
        return Value;
    }
    public object ConvertValueToJSON(IServiceHub serviceHub = null)
    {
        // Get the values of the dictionary
        var vals = ConvertToJSON(serviceHub).Values;

        Debug.WriteLine(vals.GetType() + " 1");

        // Check if vals is a ValueCollection and contains exactly one element
        if (vals.Count == 1)
        {
            Debug.WriteLine(vals.GetType() + " 2");

            // Return the first and only value
            return vals.FirstOrDefault();
        }

        // Return vals if no single value is found
        return vals;
    }

    //public object ConvertValueToJSON(IServiceHub serviceHub = null)
    //{
    //    var vals = ConvertToJSON(serviceHub).Values;
    //    Debug.WriteLine(vals.GetType() + " 1");
    //    if (vals.GetType() == typeof(IDictionary<string,object>) || vals.GetType() == typeof(Dictionary<string, object>))
    //    {
    //        if (vals.Count == 1)
    //        {
    //            Debug.WriteLine(vals.GetType() + " 2");
    //            return vals.FirstOrDefault();
    //        }
    //    }
    //    return vals;
    //}

    public object Value { get; private set; }
}
