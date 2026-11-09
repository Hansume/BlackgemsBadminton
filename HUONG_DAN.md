# SHUTTLE ARENA – Cầu lông 3D online (Unity 6 · URP · WebGL)

## 1. Cấu trúc dự án

```
Assets/_Badminton/
├─ Scripts/
│  ├─ Core/        [Badminton.Core]       Logic thuần C#: kích thước sân, vật lý cầu, cú đánh, luật & tính điểm
│  ├─ Runtime/     [Badminton.Runtime]    Court, Shuttlecock, Player (+Animator procedural), ShotSystem, AI,
│  │                                       Scoring (MatchController), CameraRig, Audio, Replay, UI, Practice
│  ├─ Networking/  [Badminton.Networking] Netcode for GameObjects + Relay/Lobby (Sessions) – asmdef riêng
│  └─ Editor/      [Badminton.Editor]     Công cụ dựng scene, tổng hợp âm thanh, build WebGL
├─ Tests/EditMode/ Unit test cho luật thi đấu (14 test)
├─ Settings/       ScriptableObject cấu hình: CameraRig, Shuttle, PlayerTuning, Shots/*, AI/*, MatchRules, UITheme, AudioLibrary
├─ Art/            Material, mesh, texture, âm thanh (tự sinh), font Barlow (OFL)
└─ Scenes/         Game.unity (menu + trận đấu), Practice_Wall.unity (tập với tường), Court.unity (sân gốc)
```

Menu **Badminton** trong Unity Editor dựng lại mọi thứ bằng code:
`1. Tạo scene sân đấu` → `3. Tạo scene GĐ2: Tập với tường` → `5. Tạo scene chính (Game.unity)`.
Thông số trong `Settings/` **không bị ghi đè** khi dựng lại.

## 2. Điều khiển

| Phím | Chức năng |
|---|---|
| WASD / mũi tên | Di chuyển (W = về phía lưới) |
| J | Cầu dài: cầu cao (≥ 1.6 m) ⇒ Clear, cầu thấp ⇒ Lift |
| K | Smash (cầu ≥ 1.9 m, cao quá thì tự bật nhảy) |
| L | Cầu ngắn: cầu cao (≥ 1.6 m) ⇒ Drop, cầu thấp ⇒ Net shot (đứng xa lưới ⇒ cú đỡ ngắn) |
| W/A/S/D khi đánh | Điều hướng cầu: A/D sang ngang, W/S dài/ngắn |
| Space | Giao cầu cao (giữ W = giao cầu ngắn) |
| C | Đổi camera người chơi ↔ broadcast |
| Esc | Tạm dừng / quay lại |

Đổi phím trong **Cài đặt** (lưu lại cho lần sau). Gamepad được hỗ trợ sẵn.

## 3. Cấu hình online (Unity Gaming Services) – làm 1 lần

Chơi online qua Internet dùng **Relay** (WebSocket bảo mật – bắt buộc cho trình duyệt) + **Lobby** (mã phòng, ghép trận).

- [ ] Unity Editor → **Edit > Project Settings > Services**: đăng nhập, chọn/tạo **Unity Cloud project** và liên kết (project hiện đã có Project ID).
- [ ] Mở **https://cloud.unity.com** → chọn project → **Products**:
  - [ ] Bật **Relay** (gói miễn phí đủ để test).
  - [ ] Bật **Lobby**.
  - [ ] **Authentication → Identity providers**: bật **Anonymous**.
- [ ] Không cần cấu hình gì thêm trong scene – `OnlineService` tự tạo NetworkManager khi chạy.

**Chưa cấu hình UGS?** Vẫn chơi online được bằng **Kết nối trực tiếp (LAN)**: một máy PC/Editor bấm *TẠO*, máy kia nhập IP rồi bấm *VÀO* (cổng 7777, WebSocket – bản WebGL cũng vào được, nhưng trình duyệt **không** làm chủ phòng LAN được).

### Link mời (bản web)
Khi chủ phòng tạo phòng trên bản web, phòng chờ hiện **Link mời** dạng `https://<trang-game>/index.html?room=ABC123`.
Người nhận mở link là tự vào phòng (vẫn phải bấm *Sẵn sàng*). Thêm `&ai=1` để AI tự chơi thay (dùng khi test).

### Test 2 tab trình duyệt
1. Build WebGL (mục 4) và mở 2 tab `http://localhost:8000` (mỗi tab tự đăng nhập một người chơi ẩn danh khác nhau).
2. Tab 1: **Chơi online → Tạo phòng** → ghi lại mã 6 ký tự.
3. Tab 2: nhập mã → **Vào phòng**. Cả hai bấm **Sẵn sàng** ⇒ trận bắt đầu.
   Hoặc cả hai bấm **Ghép trận nhanh**.

### Test mạng tự động (không cần người)
Bản build PC (Build Settings → Windows, Development) nhận các tham số sau; cả hai bên tự bấm *Sẵn sàng*,
`-bmAI` cho AI điều khiển nhân vật. Mỗi điểm được ghi vào log dạng `[Online] RALLY ... ping=...`.
```
# LAN / WebSocket trực tiếp
ShuttleArena.exe -bmHost -bmAI
ShuttleArena.exe -bmJoin 127.0.0.1 -bmAI

# Internet qua Relay + Lobby: máy A tạo phòng, mã phòng ghi trong log ("ROOM_CODE=XXXXXX")
ShuttleArena.exe -bmCreate -bmAI -logFile host.log
ShuttleArena.exe -bmCode XXXXXX -bmAI -logFile client.log
```
Giao thức Relay: **WSS trên WebGL**, **DTLS trên PC** – Relay cho phép trộn lẫn, nên người chơi web và PC vào chung phòng được.

## 4. Build WebGL

- Unity: **Build > Web > Build WebGL** (tự áp dụng thiết lập tối ưu, output `Builds/WebGL`).
- Hoặc dòng lệnh (đóng Editor trước): `powershell -ExecutionPolicy Bypass -File Tools\build_webgl.ps1`
- Chạy thử: `python Tools/serve_webgl.py` → http://localhost:8000 (không mở `index.html` trực tiếp).

### Các tối ưu đã áp dụng
| Hạng mục | Thiết lập |
|---|---|
| Nén | Brotli + *decompression fallback* (chạy trên mọi host tĩnh) |
| Code | IL2CPP *Optimize Size*, cấu hình *Master*, Managed Stripping *Medium* (+`link.xml` giữ UGS/Netcode), *Strip Engine Code* |
| Cache | *Data caching* + tên file theo hash (cập nhật bản mới không dính cache cũ) |
| Render | URP Mobile: tắt HDR, tắt shadow map (dùng blob shadow), 1 directional light, không post-processing, SRP Batcher + GPU Instancing, sân & khán đài *Batching Static* |
| Đồ hoạ | Cài đặt *Thấp* = render scale 0.7 cho máy yếu / màn hình HiDPI |
| Âm thanh | Tổng hợp bằng code, mono 22 kHz Vorbis (~vài trăm KB) |
| Mô phỏng | Tick cố định 60 Hz, `maximumDeltaTime` 0.1 s chống "vòng xoáy chết" khi máy yếu |

## 5. Deploy lên itch.io
1. Build WebGL (mục 4).
2. Nén **nội dung bên trong** `Builds/WebGL` (file `index.html` phải nằm ở gốc file zip) thành `ShuttleArena.zip`.
3. itch.io → **Upload new project**:
   - *Kind of project*: **HTML**.
   - Upload `ShuttleArena.zip` → tick **This file will be played in the browser**.
   - *Viewport dimensions*: **1280 × 720**, bật **Fullscreen button**.
   - *Frame options*: để mặc định (game không dùng đa luồng nên **không** cần bật SharedArrayBuffer).
4. Save → mở trang game để kiểm tra. Online qua Relay hoạt động trên itch.io (HTTPS + WSS).

## 6. Deploy lên Unity Play
1. Unity → **Window > Package Manager** → cài **WebGL Publisher** (`com.unity.connect.share`).
2. **Publish > WebGL Project** → chọn thư mục `Builds/WebGL` (hoặc để Publisher tự build) → **Publish**.
3. Đăng nhập Unity ID khi được hỏi; Unity Play trả về đường link chia sẻ.

## 7. Kiến trúc mạng (tóm tắt)
- **Server-authoritative**: host mô phỏng quả cầu, cả 2 nhân vật và phán quyết điểm (`MatchController.IsAuthority`).
- **Client-side prediction**: client tự di chuyển nhân vật của mình ngay lập tức, hiệu chỉnh nhẹ theo vị trí host xác nhận.
- **Interpolation**: đối thủ hiển thị trễ 100 ms, nội suy giữa các snapshot 30 Hz.
- **Lag compensation**: cú đánh của client kèm tick; host tua lại lịch sử cầu/người chơi (tối đa 150 ms) để xét trúng/trượt.
- **Quỹ đạo tất định**: mỗi cú đánh chỉ gửi (vị trí, vận tốc, tick); hai máy tự mô phỏng bằng cùng `ShuttlePhysics`, đồng bộ hiệu chỉnh 6 Hz.
- **Mất kết nối**: host tạm dừng trận 30 giây chờ client nối lại (đánh lại điểm từ lượt giao), quá hạn ⇒ xử thua.
- **Tick**: mô phỏng & mạng 60 Hz; Relay qua **WSS** (WebGL không hỗ trợ UDP).

## 8. Chế độ GIẢI ĐẤU (chơi đơn, offline)

Vào từ nút **GIẢI ĐẤU** ở màn hình chính → Mùa giải mới / Tiếp tục mùa giải / Giải đấu đơn lẻ / Giải tùy chỉnh.
Mọi đối thủ là AI, không dùng mạng (không tạo NetworkManager). Tên VĐV, tên giải, cấp giải đều hư cấu; chỉ cờ quốc gia là thật.

### Việc làm tay trong Unity (một lần)
- [ ] Menu **Badminton > Giải đấu > Tạo dữ liệu giải đấu** → tạo `Assets/_Badminton/Resources/Tournament/TournamentSettings.asset` + `AthleteDatabase.asset` + `Settings/AI/AI_VeryHard.asset`.
- [ ] Menu **Badminton > 5. Tạo scene chính** (dựng lại `Game.unity` để có 4 nhân vật – cần cho đánh đôi).
- [ ] (Tuỳ chọn) chỉnh số liệu trong `TournamentSettings.asset` → mục *balance* (mọi con số cân bằng đều có tooltip tiếng Việt).
- [ ] Muốn sinh lại database theo seed khác: sửa *balance > database > seed* rồi chạy **Badminton > Giải đấu > Sinh lại database VĐV**.

### Cấu trúc code
| Thư mục | Nội dung |
|---|---|
| `Scripts/Core/Rules` | `MatchState` (luật 3×21 + lịch sử tỷ số game), `DoublesServeState` (luật giao cầu đôi), `RallyJudge` (sân đơn/đôi) |
| `Scripts/Runtime/Scoring/ScoringRules.cs` | ScriptableObject luật chung cho mọi chế độ (nghỉ chính thức 60/120 s cho giải đấu, nghỉ nhanh cho online) |
| `Scripts/Tour` (asmdef `Badminton.Tour`, C# thuần) | Database + bộ sinh tên, `MatchResultGenerator`, `DrawEngine` (hạt giống/bốc thăm), `TourEngine` (loại trực tiếp, vòng loại, vòng bảng), `SeasonEngine` (lịch, BXH 52 tuần, trôi rating, lưu) |
| `Scripts/Runtime/Tournament` | `TournamentSettings`, `TournamentSession` (lưu/tải slot), `TournamentUI*.cs` (mọi màn hình), `TourWidgets` (cờ, bảng, cuộn) |
| `Scripts/Runtime/AI/DoublesTeam.cs` | AI đồng đội: đội hình tấn công/phòng thủ, nhường cầu cho người chơi |

### Quy tắc chính
- Trận AI vs AI **không mô phỏng**: chỉ sinh tỷ số cuối (`MatchResultGenerator`, seed lưu trong file save). Kết quả sinh sẵn khi vòng bắt đầu nhưng chỉ công bố sau trận của người chơi.
- **Menu chính → NHÂN VẬT**: chỉnh giới tính (Nam/Nữ), kiểu tóc (3 kiểu mỗi giới tính), màu áo / màu quần / màu vợt / màu giày (4 hàng riêng, dùng chung bảng 10 màu: đỏ, cam, vàng, xanh lá, xanh ngọc, xanh dương, tím, hồng, trắng, đen), quốc tịch (15 nước, có Việt Nam); có xem trước 3D, lưu ngay (PlayerPrefs `bm.char.*`). Dùng trong Giải đấu và Đấu với AI (online không áp dụng).
- Giới tính quyết định nội dung được chọn: Nam ⇒ Đơn nam / Đôi nam / Đôi nam nữ; Nữ ⇒ Đơn nữ / Đôi nữ / Đôi nam nữ. Ở đôi nam nữ người chơi đóng đúng vai theo giới tính, đồng đội AI là người còn lại; đội người chơi mặc màu quần áo đã chọn.
- Không chọn VĐV: người chơi luôn thi đấu bằng **tên đặt ở menu chính** (đánh đôi: đồng đội AI). Chọn nội dung xong là vào thẳng giải; suất thi đấu được gán tự động ở vị trí *balance > player > startRankFraction* của bảng xếp hạng (mặc định giữa bảng).
- Độ khó AI chỉ theo rating: <40 Dễ · 40–64 Thường · 65–84 Khó · ≥85 Rất khó.
- Thoát giữa trận giải (Esc → BỎ CUỘC) = xử thua.
- Mùa giải lưu vào PlayerPrefs (WebGL: IndexedDB), 3 slot, tự lưu sau mỗi bước; giải đơn lẻ/tùy chỉnh không lưu.
- Thoát game / tải lại trang khi đang đánh trận giải (mùa giải) ⇒ lần tải sau trận đó bị xử thua.
- Lịch mùa giải: 19 giải + chung kết; một số tuần có 2 giải song song (chọn 1 hoặc nghỉ). Tuần song song không có vòng loại; giải cấp cao chọn người trước, một VĐV không dự 2 giải cùng tuần.
- Đánh đôi: chỉ người nhận được đỡ cú giao cầu (đồng đội đỡ thay ⇒ mất điểm "SAI NGƯỜI ĐỠ GIAO CẦU").
- NetworkManager chỉ được tạo khi bấm "CHƠI ONLINE" (hoặc mở bằng link mời / tham số test) – các chế độ offline không có NetworkManager.
- Thông số đánh đôi (AI đồng đội, vị trí, camera) và nhịp hiệu ứng nằm trong `TournamentSettings.asset` → *doubles*, *ui*; giải tùy chỉnh → *balance > custom*.

### Test
- EditMode tests (Test Runner): luật điểm, luật giao cầu đôi, tỷ số luôn hợp lệ (100 000 trận), 1000 giải (hạt giống 1 vô địch ≈ 30%), vòng loại, vòng bảng, mùa giải + lưu/tải.
