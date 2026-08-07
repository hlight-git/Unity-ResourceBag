# 04 — Cross Bag

Bag người chơi nhân đôi coin khi bag Event còn vé, thông qua injector của project.

Kéo `CrossBagSample.prefab` vào scene, bấm Play và xem Console.

## Nối vào DI của bạn — một method

`IBagInjector` chỉ có một verb, nên cầu nối tới `DependencyInjector` của
`com.hlight.dependency-inversion` là một dòng forward:

```csharp
sealed class BagInjector : IBagInjector
{
    readonly DependencyInjector _injector;
    public BagInjector(DependencyInjector injector) => _injector = injector;
    public void Inject(object target) => _injector.Inject(target);
}
```

Khai ở **phía project**, đừng đưa `IBagInjector` vào package DI — asmdef của nó không
reference gì, thêm vào sẽ buộc một package DI phổ dụng phụ thuộc ResourceBag.

## Dependency đi vào rule ở đâu

`Attach` — đó là factory của rule, và nó là code của bạn:

```csharp
public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
{
    if (owner == null) return null;

    var rule = new Instance(this, owner, bag);
    bag.Injector?.Inject(rule);
    return rule;
}
```

Phía scope nêu đích danh loại rule mình nuôi, nên đọc một file là biết ai cấp cho ai:

```csharp
public sealed partial class GameScope : IDependencyResolvable<EventBuffRule.Instance>
{
    public void ResolveDependenciesFor(EventBuffRule.Instance t) => t.EventBag = () => _eventBag;
}
```

## Vì sao là `Func<ResourceBag>` chứ không phải `ResourceBag`

`Attach` chạy **bên trong constructor** của `ResourceBag`. Bag "event" có thể được dựng sau
bag "player", và nếu resolver đẩy vào một instance thì rule giữ lại đúng cái nó thấy ở thời
điểm đó — thường là `null`, vĩnh viễn.

Đẩy `Func` thì thứ tự dựng hết quan trọng, và điều đó **lộ ra ở kiểu của field** — trước đây
nó nằm ẩn trong cờ "đã resolve chưa" bên trong rule. `Tests/Core/ResourceBagInjectionTests.cs`
có test cho cả hai chiều: bản đẩy instance bỏ mất dependency sinh muộn, bản đẩy `Func` thì không.

## Không còn key

Locator cũ phân biệt hai bag cùng kiểu bằng key (`"event"` vs `"player"`). Resolver push là
per target type nên không có key — nhưng cũng không cần: resolver đã nêu tên chính xác loại
rule nó cấp, và mỗi rule biết nó muốn bag nào.
