using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Snet.Yolo.Tasks.Components;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>真实 Tasks 宿主的依赖注入完整性：组件用 <c>[Inject]</c> 或 <c>@inject</c> 声明的服务必须全部已注册，否则页面打开时才会抛出异常。</summary>
[Collection("Database")]
public sealed class ComponentServiceRegistrationTests(DevelopmentTasksFactory factory) : IClassFixture<DevelopmentTasksFactory>
{
    /// <summary>枚举程序集内全部组件的注入属性（含私有与基类声明），逐个在宿主容器中解析。</summary>
    [Fact]
    public void EveryInjectedComponentServiceIsRegistered()
    {
        using var scope = factory.Services.CreateScope();
        var missing = new List<string>();
        var components = typeof(App).Assembly.GetTypes().Where(static type => typeof(IComponent).IsAssignableFrom(type) && !type.IsAbstract);
        foreach (var component in components)
        {
            foreach (var property in InjectedProperties(component))
            {
                if (scope.ServiceProvider.GetService(property.PropertyType) is null)
                {
                    missing.Add($"{component.Name}.{property.Name}（{property.PropertyType.FullName}）");
                }
            }
        }
        Assert.True(missing.Count == 0, "以下组件依赖没有注册服务：" + string.Join("；", missing));
    }

    /// <summary>沿继承链收集带有注入特性的属性；带键的注入不在此检查范围内。</summary>
    /// <param name="component">组件类型。</param>
    /// <returns>需要由容器提供值的属性。</returns>
    private static IEnumerable<PropertyInfo> InjectedProperties(Type component)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var type = component; type is not null && type != typeof(object); type = type.BaseType)
        {
            foreach (var property in type.GetProperties(flags))
            {
                if (property.GetCustomAttribute<InjectAttribute>() is { Key: null }) { yield return property; }
            }
        }
    }

    /// <summary>自检：测试确实扫描到了足够多的注入点，避免反射条件写错后一直“通过”。</summary>
    [Fact]
    public void ScanFindsInjectionPoints()
    {
        var count = typeof(App).Assembly.GetTypes()
            .Where(static type => typeof(IComponent).IsAssignableFrom(type) && !type.IsAbstract)
            .Sum(type => InjectedProperties(type).Count());
        Assert.True(count > 50, $"只扫描到 {count} 个注入点，扫描条件可能有误。");
    }
}
