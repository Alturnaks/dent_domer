using System.Reflection;
using Dental.Domain.Common;
using Serilog.Core;
using Serilog.Events;

namespace Dental.Api.Infrastructure;

/// <summary>Маскирует свойства с [Sensitive] (ИИН, телефоны) при деструктуризации объектов в логах.</summary>
public sealed class SensitiveDestructuringPolicy : IDestructuringPolicy
{
    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, out LogEventPropertyValue result)
    {
        var type = value.GetType();
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        if (!props.Any(p => p.GetCustomAttribute<SensitiveAttribute>() is not null))
        {
            result = null!;
            return false;
        }

        var list = new List<LogEventProperty>();
        foreach (var p in props.Where(p => p.GetIndexParameters().Length == 0))
        {
            object? v;
            try { v = p.GetValue(value); } catch (TargetInvocationException) { continue; }
            if (p.GetCustomAttribute<SensitiveAttribute>() is not null && v is string s)
                v = Mask(s);
            list.Add(new LogEventProperty(p.Name, propertyValueFactory.CreatePropertyValue(v, destructureObjects: false)));
        }
        result = new StructureValue(list, type.Name);
        return true;
    }

    public static string Mask(string s) => s.Length <= 4 ? "***" : new string('*', s.Length - 4) + s[^4..];
}
