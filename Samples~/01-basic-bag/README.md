# 01 — Bag cơ bản

Hai resource: Heart (giới hạn 5) và Coin (không giới hạn).

## Chạy

Kéo **`BasicBagSample.prefab`** vào scene → bấm Play. Không cần setup gì.

## Log mong đợi

```
[Bag] Heart 0→3 (test)
[Bag] Coin 0→100 (test)
[Bag] Heart 3→5 (overflow_test)     // cộng 10 nhưng cap là 5
[Bag] Heart 5→5 (_overflow)         // event delta = 0, báo "đã đầy"
[Bag] Heart 5→4 (use)
```

## Resource trong folder

| Resource | Là gì |
|---|---|
| `HeartDef`, `CoinDef` | `BasicBagDef` — def cụ thể, `Key` = Heart / Coin |
| `BasicBagBlueprint` | Phạm vi của bag: 2 def, cap 5 và 0 |
| `BasicBagSample.prefab` | GameObject đã nối sẵn (chỉ cần blueprint) |

## Bốn điều cần nhớ

1. **Gọi bag bằng enum**, đừng giữ reference `ResourceDefinition`:

   ```csharp
   _bag.Add(BasicBagResourceId.Heart, 3, "test");     // nên
   _bag.Add(heartDef, 3, "test");                  // đừng — phải kéo một slot cho mỗi def
   ```

   Nhờ vậy MonoBehaviour này chỉ có **một** slot: blueprint. Sai chính tả cũng không xảy ra được.

2. **`ResourceDefinition` là abstract** → project phải có một lớp con một dòng (`BasicBagDef.cs`).
3. **`maxAmount` nằm ở blueprint**, không ở def. Cap là chính sách của scope: cùng một Heart có thể cap 5 ở bag này và 10 ở bag khác.
4. **`Id` là tên thành viên enum** → đổi tên thành viên sẽ làm mọi save cũ mồ côi.

Giữ reference def chỉ hợp lý ở hai chỗ: slot cho designer kéo trên MonoBehaviour **không phải** bag (ví dụ config shop), và bên trong rule tự viết. Sample 04 có ví dụ thứ hai.

## Tiếp theo

Sample **02** nếu cần hồi phục theo thời gian. Sample **05** giải thích kỹ vì sao gọi bằng enum và cách tra cứu hoạt động.
