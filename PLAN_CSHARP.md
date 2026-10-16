# LSLGameCS — C# Port Planı

> Kaynak proje: `E:\GitHub\LSLGame` (Python 3.12) — referans uygulama. Bu klasör onun C# (.NET) kopyasıdır.
> Bu belge yalnızca plandır. Amaç: aynı davranışı, aynı LSL sözleşmelerini ve aynı testleri C#'ta üretmek.

## 1. Hedefler ve Kapsam
- EEG alımı → işleme → kod çözme → `BCI_Control` → oyun zincirinin C# eşdeğeri.
- LSL akış sözleşmeleri **birebir korunur** (`docs/control_contract.md`); Python ve C# bileşenleri birlikte çalışabilir (Python simülatör + C# oyun gibi).
- Sayısal çıktılar (filtre, ERD, CSP/LDA) Python ile **parite testleriyle** doğrulanır.
- Marker adları yalnızca ASCII (PLAN.md kuralı).

## 2. Teknoloji Seçimleri
| İhtiyaç | Python | C# karşılığı (öneri) |
|---|---|---|
| Çalışma zamanı | Python 3.12 | .NET 10 (SDK 10.0.402 kurulu) |
| LSL | pylsl | liblsl'e P/Invoke sarmalayıcı veya NuGet LSL paketi (doğrulanacak) |
| Sayısal | NumPy/SciPy | MathNet.Numerics (+ kendi SOS/biquad filtre) |
| Sınıflandırma | scikit-learn | Kendi CSP + LDA (+ Riemann ops.) |
| XDF | pyxdf | Kendi XDF okuyucu / uygun NuGet (doğrulanacak) |
| Oyun/UI | pygame | Raylib-cs (öneri) / MonoGame |
| Yapılandırma | YAML | YamlDotNet |
| Test | pytest | xUnit + FluentAssertions |

## 3. Çözüm Yapısı
```
LSLGameCS/
├─ LSLGameCS.sln
├─ config/  docs/            # Python'dan kopyalanır
├─ src/
│  ├─ LslGame.Core/          # ortak tipler, config, ASCII marker kuralı
│  ├─ LslGame.Acquisition/   # stream_receiver
│  ├─ LslGame.Processing/    # filters, spatial, artifacts, epochs, erd, recording
│  ├─ LslGame.Models/        # CSP, LDA, pipelines, evaluation, offline_analysis
│  ├─ LslGame.Online/        # decoder
│  ├─ LslGame.Control/       # contract, keyboard_source
│  ├─ LslGame.SignalQuality/ # quality_metrics
│  ├─ LslGame.Simulation/    # synthetic_eeg, xdf_replay, offline_dataset
│  ├─ LslGame.Calibration/   # paradigm, run_calibration
│  └─ LslGame.Game/          # ball_roll
├─ tests/LslGame.Tests/
└─ tools/parity/             # Python↔C# karşılaştırma
```

## 4. Modül Eşlemesi
| Python (satır) | C# | Öncelik |
|---|---|---|
| control/contract.py (295) | Control/ControlContract | 1 |
| acquisition/stream_receiver.py (412) | Acquisition/StreamReceiver | 1 |
| simulation/synthetic_eeg.py (475) | Simulation/SyntheticEegPublisher | 1 |
| control/keyboard_source.py (201) | Control/KeyboardSource | 2 |
| game/ball_roll.py (649) | Game/BallRollGame | 2 |
| signal_quality/quality_metrics.py (231) | SignalQuality/QualityMetrics | 2 |
| processing/filters.py (213), spatial.py (90), artifacts.py (156), epochs.py (241), erd_analysis.py (376), recording.py (235) | Processing/* | 3 |
| simulation/xdf_replay.py (202), offline_dataset.py (450) | Simulation/* | 3 |
| calibration/paradigm.py (499), run_calibration.py (743) | Calibration/* | 4 |
| models/pipelines.py (391), evaluation.py (446) | Models/* | 4 |
| models/offline_analysis.py (426) | Models/OfflineAnalysis | 5 |
| online/decoder.py (229) | Online/OnlineDecoder | 5 |

## 5. Aşamalar
**A — İskelet:** sln + projeler, `Directory.Build.props`, config/docs kopyası, LSL hello testi (Python↔C# akış alışverişi).
Çıkış: `dotnet build`/`dotnet test` yeşil.

**B — Sözleşme + Simülasyon + Oyun (Faz 0/2/6):** ControlContract, SyntheticEegPublisher (11 kanal, 250 Hz, aynı meta veri, tuşlar l r d u s b), KeyboardSource, BallRollGame (`BCI_Markers` ASCII).
Çıkış: Python↔C# çapraz çalışma; işaret–EEG hizalaması −4…+12 ms.

**C — İşleme + Sinyal Kalitesi:** filtreler (bandpass, 50 Hz notch), CAR, artefakt, epoch, QualityMetrics.
Çıkış: golden dosya parite testleri (göreli tolerans ≤1e-6; filtre ≤1e-4).

**D — Kalibrasyon (Faz 3):** paradigma, kayıt, ASCII marker; hizalama +0,4…+3,6 ms.
Çıkış: simülatörle uçtan uca oturum; dosya Python'da okunabilir.

**E — Modeller + Çevrimiçi Kod Çözücü (Faz 4/5):** CSP+LDA, CV + permütasyon testi, Python modelini JSON ile içe aktarma, OnlineDecoder (kayan pencere, yumuşatma, güven eşiği).
Çıkış: sinyalsiz veride doğruluk ~%40–60, p<0.05 tohum ≤1/6; Python ile karar örtüşmesi ≥%99.

## 6. Açık Kararlar (Sizden)
1. Oyun/UI: Raylib-cs mi, MonoGame mi, WPF/Avalonia mı? *(Öneri: Raylib-cs)*
2. Kapsam: tam port mu, yoksa C# yalnızca çalışma zamanı (simülatör+kod çözücü+oyun), çevrimdışı analiz Python'da mı? *(Öneri: ikincisi; MNE maliyetli)*
3. LSL kütüphanesi: NuGet paketi mi, liblsl P/Invoke mu?
4. Python deposu referans olarak sürdürülecek mi? (Parite testleri bunu varsayar)
5. Yalnızca Windows mu?

## 7. Riskler
| Risk | Önlem |
|---|---|
| NumPy/SciPy/MNE karşılığı yok | Çevrimdışı analiz Python'da kalsın; model JSON ile aktarılsın |
| IIR filtre sayısal farkları | SOS katsayılarını Python'dan dışa aktar; golden testler |
| LSL .NET sarmalayıcı bakımsız | liblsl P/Invoke yedeği |
| GC kaynaklı gecikme | Ön tahsis, yüksek öncelikli iş parçacığı |
| Ajan uydurma rapor eğilimi (DEVAM.md) | Test çıktısı dosyaya yazılır, `dotnet test --list-tests` ile karşılaştırılır; sahte nesnesiz entegrasyon testi şart; LSL komutlarına zaman sınırı |

## 8. Test Stratejisi
- Her Python test dosyasına xUnit karşılığı (adlar eşlenir).
- Golden/parite testleri (`tools/parity`).
- Gerçek LSL ile simülatör→alıcı→kod çözücü→oyun entegrasyonu.
- `dotnet test` en az 2 kez çalıştırılır.

## 9. Çalışma Düzeni
- Bu klasörde ayrı git deposu (`git init`).
- Görevler `tasks/` altında aşamaya göre numaralı.
- Her aşama sonunda çıkış kriteri ölçülür, sonra commit.

## 10. Sonraki Adım
Bölüm 6 kararları netleşince **Aşama A**'yı başlat.