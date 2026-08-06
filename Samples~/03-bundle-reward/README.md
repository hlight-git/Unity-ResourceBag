# 03 — Bundle nở ra nhiều resource

Một "chest" khi Add sẽ nở thành 100 coin + 5 gem + 1 hammer. Bản thân chest không được lưu.

## Chạy

Kéo **`BundleRewardSample.prefab`** vào scene → bấm Play.

## Log mong đợi

```
[Bag] Coin +100 (_resolve_bundle)
[Bag] Gem +5 (_resolve_bundle)
[Bag] Hammer +1 (_resolve_bundle)
Coins=100 Gems=5 Hammers=1 Chests=0
```

`Chests=0` là đúng: rule đặt `SkipPrimary` nên chest không vào bag.

## Resource trong folder

| Resource | Là gì |
|---|---|
| `CoinDef`, `GemDef`, `HammerDef` | `BundleDef` — 3 resource thành phần |
| `ChestBundleRule` | `BundleResolveRule` — entries (Coin,100), (Gem,5), (Hammer,1) |
| `ChestRewardDef` | `BundleDef`, `Rules` = [ChestBundleRule] |
| `BundleBlueprint` | Cả 4 def |
| `BundleRewardSample.prefab` | Đã nối sẵn (chỉ cần blueprint) |

## Bốn điều cần biết khi làm bundle thật

1. **Số lượng nhân theo lượng Add.** `Add(chest, 3)` cho 300 coin. Entry là "mỗi một đơn vị bundle".
2. **Thứ tự rule có ý nghĩa.** Nếu def chest còn một rule nhân hệ số chạy *trước*, lượng đã nhân sẽ dùng để nở bundle — thường không phải ý bạn. Đặt bundle rule ở đầu `Rules`.
3. **Bundle lồng nhau được**, nhưng độ sâu chặn ở 5. Vượt thì package `LogError` và **bỏ** intent đó, không throw — nên kiểm Console nếu phần thưởng lồng sâu tự nhiên mất.
4. **Entry trỏ tới def ngoài blueprint vẫn được cộng** nhưng **không được lưu** — nó sẽ mất khi khởi động lại. Nhớ liệt kê mọi def thành phần vào `Resources` của blueprint.
