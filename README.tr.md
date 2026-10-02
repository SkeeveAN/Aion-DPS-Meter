🇬🇧 [English](README.md) · 🇩🇪 [Deutsch](README.de.md) · 🇪🇸 [Español](README.es.md) · 🇫🇷 [Français](README.fr.md) · 🇷🇺 [Русский](README.ru.md) · 🇵🇱 [Polski](README.pl.md) · 🇹🇷 **Türkçe** · 🇨🇳 [中文](README.zh.md)

# Aion DPS Meter

Topluluk boss sıralamaları ve karakter profilleri içeren web sitesi: **https://aiondps.com**

**Aion 2** için bir hasar/iyileştirme sayacı. Oyunun ağ trafiğini bilgisayarınızda [Npcap](https://npcap.com)
sürücüsü üzerinden okur — pasif olarak: hiçbir paket göndermez, oyun sürecine ya da belleğine dokunmaz. Siz yüklemedikçe
oyununuzdan hiçbir şey bilgisayarınızdan çıkmaz (bkz. [Yüklemeler](#yüklemeler)); bunun yanında GitHub'a daha yeni bir
sürüm olup olmadığını soran, kapatılabilen güncelleme denetimi gönderilir; bkz. [Güncellemeler](#güncellemeler).

> Klasik Aion (Chat.log tabanlı) artık sayacın parçası değil. Onu destekleyen son sürüm
> [`aion1-included`](../../tree/aion1-included) dalında duruyor.

## Kurulum

1. [Npcap](https://npcap.com) sürücüsünü kurun (sayaç oyunun trafiğini görmek için ona ihtiyaç duyar; yükleyiciye dahil değildir).
2. [Son sürümden](../../releases/latest) `AionDpsMeter-win-Setup.exe` dosyasını indirip çalıştırın. Kullanıcı profilinize
   kurulur ve sayacı başlatır — yönetici hakkı ve .NET gerekmez.
3. Sayacı başlatın, ardından karakterinizle giriş yapın. Sayaç karakterinizi, ekipmanınızı, becerilerinizi ve Daevanion
   panolarınızı oyunun kendisinden okur; sunucu otomatik algılanır.

## Kullanım

Kayıt, sayaç çalışır çalışmaz başlar. **Duraklatma erteler değil, atar**: duraklatma sırasındaki olaylar sonsuza dek
kaybolur, devam etmek izlemediğiniz bir savaşı asla yeniden oynatmaz.

### Görünümler

- **Dmg** — oyuncu başına hasar, toplam ve DPS ile, sınıf simgeleri ve sıralanabilir liste. **Mob/Boss** filtresi sütunu genel
  DPS ile hedef başına gerçek **iDPS** arasında değiştirir. Bosslar oyunun verisinden tanınır ve adıyla gösterilir.
  Bir oyuncuya **çift tıklamak** beceri dökümünü açar.
- **Karakter** (kişi simgesi) — kendi karakterinizin penceresini açar: profil, eşya seviyeli ve yükseltmeli ekipman,
  seviyeli beceriler ve Daevanion panoları. Son girişinizi saklar, bu yüzden asla boş değildir.

### Hide UI (yer paylaşımı)

Pencereyi oyunun üstünde duran, tıklamayı geçiren küçük etiketlere çevirir — oyuncu başına bir tane: ad, hasar ve DPS.
**Ctrl+Alt+H** ile her yerden açılıp kapanır.

### Kopyalama

**Copy**, panoya tek satırlık, sohbete hazır bir sıralama koyar (`Ad 1.234.567 (890), …`); **Copy All** Discord için bir
Markdown tablosu verir.

### Sohbet komutları

`.ui` (yer paylaşımı), `.pause` / `.resume`, `.dmg` (sıralamayı kopyala) ve `.cleardmg` (oturumu temizle). İşleyici bunları
yalnızca kendi karakterinizden kabul eder. Aion 2'nin sohbeti henüz çözülmediği için şimdilik bir şey yapmazlar.

## Yüklemeler

- **Boss savaşları**, yükle'ye (düğme veya Session menüsü) tıkladığınızda yüklenir: boss, katılan oyuncular, hasar, iyileştirme,
  alınan hasar ve beceriler. Yalnızca oyunun duyurduğu ve kataloğun tanıdığı bosslar kabul edilir.
- **Kendi karakter profiliniz** (ad, sınıf, seviye, ekipman, beceriler, Daevanion, lejyon, sunucu) girişten birkaç saniye sonra
  otomatik yüklenir, böylece sitede bulunabilirsiniz. **Ayarlar**'dan kapatılabilir.
- Yükleme yapmadıkça hiçbir şey bilgisayarınızdan çıkmaz.

## Güncellemeler

Sayaç kendini günceller. Başlangıçta ve her beş dakikada bir GitHub'dan daha yeni bir sürüm sorar, arka planda indirir ve bir
sonraki başlangıçta uygular — yükleyici ve UAC yok. Güncelleme hazır olunca altta yeşil bir satır belirir; tıklamak hemen
yeniden başlatmayı önerir. **App → Check for updates** aynısını isteğe bağlı yapar.

Denetim tek bir URL okur ve isteğin kendisi dışında hiçbir şey göndermez:

```
https://api.github.com/repos/SkeeveAN/Aion-DPS-Meter/releases
```

**Ayarlar → Güncellemeler**'den kapatılabilir; menü öğesi yine de çalışır.

## Kaynaktan derleme

```
cd Client
dotnet build
dotnet run -- selftest                              # öz denetimler (protokol, yakalama, çözme, ...)
dotnet run -- aion2-record <out.jsonl>              # oyunun trafiğini kaydet ("stop" bitirir)
dotnet run -- aion2-replay <dosya.jsonl>            # kaydı gerçek çözücüden geçirerek oynat
dotnet run -- aion2-upload-dryrun <dosya.jsonl>     # bir kaydın yüklemelerini oluştur, hiçbir şey gönderme
```

Yalnızca Windows (WPF). `Tools/aion2-dat` oyunun metin tablolarını okur (sekiz dilde adlar); README'sine bakın.

## Sunucu kuralları hakkında not

Sayaç yalnızca oyunun ağ trafiğini pasif olarak gözlemler. Yine de yayıncılar üçüncü taraf araçlar için kendi kurallarını koyar
— kullanmadan önce oyunun koşullarına göz atmakta fayda var.

## Sorumluluk reddi ve lisans

Aion 2 ve ilgili tüm adlar, metinler ve grafikler NCSOFT'un mülkiyetindedir. Bu proje NCSOFT ile bağlantılı değildir, NCSOFT tarafından desteklenmez veya onaylanmaz; oyun içeriği sahibine aittir. Kaynak kod [MIT Lisansı](LICENSE) ile yayımlanır — lisans yalnızca kodu kapsar, oyun içeriğini kapsamaz.
