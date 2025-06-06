// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization.Metadata;

namespace System.Text.Json.Serialization.Converters
{
    internal class ImmutableDictionaryOfTKeyTValueConverter<TDictionary, TKey, TValue>
        : DictionaryDefaultConverter<TDictionary, TKey, TValue>
        where TDictionary : IReadOnlyDictionary<TKey, TValue>
        where TKey : notnull
    {
        protected sealed override void Add(TKey key, in TValue value, JsonSerializerOptions options, ref ReadStack state)
        {
            Dictionary<TKey, TValue> dictionary = (Dictionary<TKey, TValue>)state.Current.ReturnValue!;

            if (options.AllowDuplicateProperties)
            {
                dictionary[key] = value;
            }
            else
            {
                if (!dictionary.TryAdd(key, value))
                {
                    ThrowHelper.ThrowJsonException_DuplicatePropertyNotAllowed();
                }
            }
        }

        internal sealed override bool CanHaveMetadata => false;

        internal override bool SupportsCreateObjectDelegate => false;
        protected sealed override void CreateCollection(ref Utf8JsonReader reader, scoped ref ReadStack state)
        {
            state.Current.ReturnValue = new Dictionary<TKey, TValue>();
        }

        internal sealed override bool IsConvertibleCollection => true;
        protected sealed override void ConvertCollection(ref ReadStack state, JsonSerializerOptions options)
        {
            Func<IEnumerable<KeyValuePair<TKey, TValue>>, TDictionary>? creator =
                (Func<IEnumerable<KeyValuePair<TKey, TValue>>, TDictionary>?)state.Current.JsonTypeInfo.CreateObjectWithArgs;
            Debug.Assert(creator != null);
            state.Current.ReturnValue = creator((Dictionary<TKey, TValue>)state.Current.ReturnValue!);
        }
    }
}
