# 05 — Gọi bằng enum, giải thích kỹ

Mọi sample đều gọi bag bằng enum. Sample này giải thích **vì sao** và cho thấy cách tra cứu hoạt động bên dưới.

## Chạy

Kéo **`EnumKeyedSample.prefab`** vào scene → bấm Play.

## Copy gì vào project

Đúng hai thứ, cho mỗi nhóm resource:

```csharp
public enum CurrencyId { Coin, Gem, Heart }

[CreateAssetMenu(menuName = "MyGame/Currency Def")]
public sealed class CurrencyDef : ResourceDefinition<CurrencyId> { }
```

Rồi gọi bằng enum, không cần kéo def vào Inspector:

```csharp
bag.Add(CurrencyId.Coin, 100, "quest");
bag.TrySpend(CurrencyId.Gem, 50, "shop");
int hearts = bag.GetAmount(CurrencyId.Heart);
```

## Vì sao nên chọn cách này

| | Giữ reference def | Gọi bằng enum |
|---|---|---|
| Slot phải kéo mỗi MonoBehaviour | Một slot mỗi def | Không có |
| Sai chính tả | Im lặng | Không sai được |
| Đổi tên resource | Vỡ dây Inspector nhiều nơi | Không vỡ gì |

> **Đánh đổi phải biết:** `Id` là **tên thành viên enum**, nên đổi tên một thành viên sẽ làm mọi save cũ **mồ côi** — người chơi mất số dư resource đó. Chốt tên enum trước khi phát hành. Đổi tên hiển thị thì dùng `Display Name`.

## Resource trong folder

| Resource | Là gì |
|---|---|
| `CoinDef`, `GemDef`, `HeartDef` | `CurrencyDef`, `Key` = Coin / Gem / Heart |
| `EnumKeyedBlueprint` | 3 def, cap 0 / 0 / 5 |
| `EnumKeyedSample.prefab` | Đã nối sẵn (chỉ cần blueprint) |

## Tra cứu hoạt động thế nào

Lần đầu gọi, blueprint quét `Resources[]`, lấy các def là `ResourceDefinition<CurrencyId>`, dựng map `{ Coin: CoinDef, ... }` rồi cache. Các lần sau là tra O(1).

Map **nằm trên blueprint, không trên bag** — nên 3 bag dựng từ cùng blueprint dùng chung một map. Hai enum khác nhau có hai map riêng.

## Khi nào **không** dùng

- **Slot cho designer kéo** trên MonoBehaviour không phải bag (config shop trỏ tới một def cụ thể) → giữ `[SerializeField] ResourceDefinition`.
- **Tham chiếu một lần** trong rule tự viết → truyền def trực tiếp.

Hai cách dùng lẫn nhau thoải mái.
