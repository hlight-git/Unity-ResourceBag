# 02 — Hồi phục theo thời gian

Tim hồi +1 mỗi 5 giây, chặn ở 5. Đây là lý do chính package tồn tại: hồi phục sống được qua lần tắt app.

## Chạy

Kéo **`WithRegenSample.prefab`** vào scene → bấm Play. Không cần setup gì.

## Resource trong folder

| Resource | Là gì |
|---|---|
| `HeartRegenRule` | `PeriodicDeltaRule` — 5 giây, +1 |
| `HeartDef` | `RegenDef`, `Key` = Heart, `Rules` = [HeartRegenRule] |
| `RegenBlueprint` | Cap 5 |
| `WithRegenSample.prefab` | GameObject đã nối sẵn |

## Lần fire đầu có thể nhảy hơn 1 — đó là đúng

Rất dễ thấy `Heart 0→2` rồi mới `2→3`. Ba điều cùng lúc:

1. Mốc fire được đặt lúc **dựng bag**.
2. `Tick()` đầu tiên đến **muộn** — boot play mode mất thời gian thật.
3. Nếu độ muộn vượt một interval, rule nợ nhiều nhịp và trả bằng **một** `Add` gộp.

Nên `+2` hiện thành **một** event `0→2`, không phải hai event. Gộp là chủ ý: 8 giờ offline với interval 1 giây là 28.800 nhịp.

**Interval càng nhỏ càng rõ.** Thử với 10–30 giây trong Editor sẽ thấy `+1` gọn.

## Điều kiện của "không dồn thời gian khi đầy"

Khi đầy cap, rule **reset** bộ đếm thay vì tích lại — nên tiêu 1 tim là phải chờ trọn 5 giây. Nhưng việc reset đó nằm **trong `OnTick`**. Nếu project chỉ tick khi một UI đang mở, bag sẽ nằm ở cap với mốc cũ và lần tick sau khi tiêu sẽ trả cả cục. Muốn lời hứa đó đúng: phải có **ít nhất một `Tick()`** giữa lúc đạt cap và lúc tiêu.

## Thời gian offline

Đồng hồ mặc định cấp tối đa **8 giờ** mỗi lần mở lại; phần vượt bị **bỏ**, không dồn. Cap đó là gờ giảm tốc, **không phải phòng thủ** — đổi giờ máy nhiều lần vẫn ăn được. Phòng thủ thật là inject `IBagClock` lấy giờ từ server.

## Lưu dữ liệu

```csharp
_bag = new ResourceBag<RegenResourceId>("regen-demo", blueprint, s_saved);   // null ở lần đầu
_bag.SaveTo(s_saved ??= new BagSnapshot());
```

Snapshot mang cả amounts, mốc fire của rule, và state đồng hồ. Sample dùng `static` nên chỉ sống trong một lần Play — đổi sang PlayerPrefs / file / `DataEntry<BagSnapshot>` để sống thật.
