# 04 — Bag đọc state của bag khác

Bag người chơi nhân đôi coin khi bag Event còn vé, thông qua Service Locator của project.

## Chạy

Kéo **`CrossBagSample.prefab`** vào scene → bấm Play.

```
[Player] Coin +200 (quest_reward)      // nhân đôi vì đang giữ 1 vé
[Player] Coin +100 (quest_reward_2)    // đã tiêu vé → hết buff
```

## Nối vào Service Locator của bạn — 0 dòng code

`IBagServiceLocator.TryProvide` có signature **giống hệt** `AServiceLocator.TryProvide` của `com.hlight.dependency-inversion`. Nên không cần adapter:

```csharp
public sealed class GameServiceLocator : AServiceLocator, IBagServiceLocator { }
```

Khai báo ở **lớp con phía project**, đừng thêm vào `AServiceLocator` — asmdef của package DI không reference gì, thêm vào đó sẽ buộc nó phụ thuộc ResourceBag.

## Resolve lúc nào — chỗ dễ sai nhất

**Resolve lần đầu dùng, không resolve trong `OnAttach`.**

`OnAttach` chạy **trong constructor của `ResourceBag`**. Resolve ở đó đòi mọi dependency phải register **trước khi** bag được dựng. Đúng với bag anh em bạn tự dựng (như sample này), **sai** với service register ở bootstrap phase sau hoặc scope chưa tồn tại — những cái đó thành `null` **vĩnh viễn và im lặng**.

```csharp
private IBonusPolicy _policy;
private bool _resolved;

private IBonusPolicy Policy
{
    get
    {
        if (_resolved) return _policy;
        _resolved = Bag.Locator != null && Bag.Locator.TryProvide(out _policy);
        return _policy;   // null tới khi được register, rồi lần sau bắt được
    }
}
```

Giữ reference locator ở đây là **đúng**. `Tests/Core/ResourceBagLocatorTests.cs` có test chứng minh pattern eager bỏ mất dependency register muộn.

## Resource trong folder

| Resource | Là gì |
|---|---|
| `CoinDef`, `EventTicketDef` | `CrossBagDef` |
| `PlayerBuffRule` | `EventBuffRule` — nhân 2 khi bag "event" còn vé |
| `PlayerBlueprint` | Coin + EventTicket |
| `EventBlueprint` | Chỉ EventTicket |
| `CrossBagSample.prefab` | Đã nối sẵn (chỉ 2 blueprint) |

Rule phải nằm trong `Rules` của **CoinDef** — owner là thứ cho rule biết nó buff cái gì. Đặt vào `ExtraRules` của blueprint thì `Attach` trả `null` và rule **im lặng không làm gì**.
