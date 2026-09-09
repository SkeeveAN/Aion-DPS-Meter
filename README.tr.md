🇬🇧 [English](README.md) · 🇩🇪 [Deutsch](README.de.md) · 🇪🇸 [Español](README.es.md) · 🇫🇷 [Français](README.fr.md) · 🇷🇺 [Русский](README.ru.md) · 🇵🇱 [Polski](README.pl.md) · 🇹🇷 **Türkçe** · 🇨🇳 [中文](README.zh.md)

# Aion DPS Meter

**AION 4.6 (OriginAion)** için, tamamen oyunun kendi `Chat.log` dosyasından çalışan bir DPS ve
ganimet ölçer.

İstemcinin kendiliğinden yazdığı bir metin dosyasını okur. Ağ trafiğini yakalamaz, oyun sürecinden
okuma veya oyun sürecine yazma yapmaz. Oynayışınıza dair hiçbir şey bilgisayarınızdan çıkmaz —
hasar sayıları yok, ganimet yok, isim yok. Gönderdiği tek şey, GitHub'a daha yeni bir sürüm olup
olmadığını soran ve kapatılabilen güncelleme kontrolüdür; bkz. [Güncellemeler](#güncellemeler).

## Kurulum

1. [En son sürümden](../../releases/latest) `AionDpsMeter-win-Setup.exe` dosyasını indirin ve
   çalıştırın. Tıklanacak bir şey yok — kullanıcı profilinize kurulur ve ölçeri başlatır.
   Yönetici hakları gerekmez, .NET gerekmez.
2. **Settings → App Settings** açın ve **Aion kurulum klasörünü** seçin — `bin64\game.dll`
   dosyasını içeren kök klasör. İletişim kutusu geçerli bir kurulum bulup bulmadığını ve orada
   zaten bir `Chat.log` olup olmadığını hemen söyler.

> **0.5.2 veya daha eskisinden mi güncelliyorsunuz?** Önce eski sürümü kaldırın (Windows Ayarları
> → Uygulamalar → *Aion DPS Meter*), sonra yeni yükleyiciyi çalıştırın. O sürümler `Program
> Files` altına kuruluyordu, bu yüzden kendilerini asla güncelleyemiyorlardı. Bu tek seferlik bir
> adım — bundan sonra güncellemeler kendiliğinden uygulanır.

### Önkoşul: istemcinin sohbet günlüğü açık olmalı

Aion, `Chat.log` dosyasını yalnızca istemci içi `g_chatlog` seçeneği etkinken yazar. Bu anahtar bu
araçta değil, oyun istemcisinde bulunur — normalde nasıl açıyorsanız öyle açın (örneğin
[ShugoConsole](https://github.com/grenadium/ShugoConsole) ile). **Aion DPS Meter bunun için oyun
sürecine asla dokunmaz**; dosya yazılmıyorsa, ölçerin okuyacak bir şeyi yoktur.

## Kullanım

Ölçer çalışır durumda ve geçerli bir Aion klasörü ayarlanmış olduğu sürece kayıt hemen başlar.

**Sohbet geçmişinizi asla okumaz.** Başlangıçta `Chat.log` dosyasının *o anki* sonuna atlar ve
yalnızca o andan itibaren yazılan satırları işler — az önce açılmış bir teyp gibi, bir arşiv
tarayıcısı gibi değil. **Duraklat, atar**, ertelemez: duraklatma sırasında yazılan satırlar
kalıcı olarak atlanır, böylece devam ettirme bilerek dışarıda bıraktığınız bir savaşı asla yeniden
oynatmaz. Geçmiş özel sohbetleriniz, lejyon sohbeti ve fısıltılar hiçbir zaman incelenmez.

### Görünümler

- **Dmg** — toplam ve DPS, sınıf simgeleri ve sıralanabilir liste ile oyuncu başına hasar.
  **Mob/Boss** filtresi sütunu genel DPS ile gerçek hedef bazlı **iDPS** (bir hedefe verilen hasar
  bölü grubun o hedefle paylaşılan mücadele süresi) arasında geçirir.
- **Loot** — kime ne düştüğü: kişi, eşya, miktar ve nadirlik derecesi. Kalıntılar ayrıca ilgili
  kişinin Uçurum Puanlarına da sayılır.

### Hide UI (kaplama)

Pencereyi, oyunun üzerinde bırakılabilecek küçük, tıklamaya duyarsız parçalara dönüştürür — oyuncu
başına bir tane, isim, hasar ve DPS gösterir. Her yerden **Ctrl+Alt+H** ile açılıp kapatılır, bu
yüzden asla geri dönüşü olmayan bir yol değildir.

### Copy

**Copy**, panoya tek satırlık, sohbete hazır bir sıralama koyar (`İsim 1.234.567 (890), …`). Loot
görünümündeyken bunun yerine Aion sohbeti için bir ganimet özeti oluşturur; **Copy All** ise bir
Discord Markdown tablosu verir.

### Oyun içi komutlar

Oyundan çıkmadan ölçeri yönetmek için bunları normal sohbet satırları gibi yazın:

| Komut | Etki |
|---|---|
| `.ui` | Hide-UI kaplamasını aç/kapat |
| `.pause` / `.resume` | kaydı durdur / sürdür |
| `.dmg` | hasar sıralamasını panoya kopyala |
| `.cleardmg` | geçerli oturumu temizle |
| `.loot` | ganimet özetini panoya kopyala |

Bunları yalnızca ayarlarda kayıtlı karakterler tetikleyebilir, bu yüzden okumadığınız bir kanalda
bir yabancının yazdığı `.cleardmg`, oturumunuzu silemez.

## Güncellemeler

Ölçer kendini günceller. Başlangıçta ve ardından her beş dakikada bir GitHub'a daha yeni bir sürüm
olup olmadığını sorar, arka planda indirir ve bir sonraki başlatmada devreye sokar. Yükleyici yok,
UAC istemi yok, tıklanacak bir şey yok. Bu, programın `Program Files` yerine kullanıcı profilinde
yaşaması sayesinde çalışır — orada kendi dosyalarını değiştirmesine izin verilir.

Bir güncelleme indirildiğinde, alttaki durum satırında yeşil bir satır belirir; üzerine tıklamak
anında yeniden başlatmayı önerir. Reddetmenin bir bedeli yoktur — sürüm zaten oradadır ve bir
sonraki normal başlatmada etkinleşir. Bilinçli olarak açılır pencere yok: pencere çalışan bir
oyunun üzerinde durur ve boss savaşının ortasında odağı çalan bir iletişim kutusu, geç bir
güncellemeden daha kötü olurdu.

**App → Check for updates**, isteğe bağlı olarak aynısını yapar ve zaten güncel olduğunuzda da
bunu söyler.

Kontrol tam olarak bir URL okur ve isteğin kendisi dışında hiçbir şey göndermez:

```
https://api.github.com/repos/SkeeveAN/Aion-DPS-Meter/releases
```

**Settings → App Settings → Updates** altından kapatılabilir. Menü öğesi yine de çalışır — o sizin
kendi isteğinizdir, programın kararı değil.

## Bu ne kadar doğru?

Aynı Sauro Supply Base koşusundan, üç farklı bilgisayarda kaydedilmiş (biri Almanca istemci) üç
`Chat.log` dosyasına karşı, gerçek boss can puanları referans alınarak doğrulandı. Boss başına
hasar, gerçek canının **%0,02 – %2,8** aralığında kalıyor:

| Boss | Gerçek Can | Ölçülen | Sapma |
|---|---|---|---|
| Muhafız Komutanı Ahuradim | 1.736.993 | 1.737.299 | +%0,02 |
| Karanlık Yutucu Derakanak | 1.343.657 | 1.347.258 | +%0,27 |
| Denetim Subayı Sayahum | 1.377.644 | 1.370.734 | −%0,50 |
| İkmal Komutanı Ranodim | 489.332 | 503.244 | +%2,84 |

Geri kalanı, günlük tabanlı hiçbir ölçerin göremeyeceği, öldürücü darbedeki aşırı hasardır. Bir
oyuncunun toplam hasarı, üç bilgisayarda da birim birim aynı çıktı.

Bilinen, zararsız iki sapma daha var: kalkanı hasarı yutan bir bosta, bu hasar yine de
günlüğe yazılır (bu yüzden toplam canını aşar), ve aynı isimli birden fazla canavar tek bir
toplamda birleştirilir.

## Kaynaktan derleme

```
dotnet build
dotnet run -- selftest                    # ayrıştırıcı ve DPS hesaplaması için iç testler
dotnet run -- chatlog <Chat.log-yolu>     # dosyayı ayrıştır ve özet yazdır
```

Yalnızca Windows (WPF). İç testler, desteklenen tüm dillerde gerçek günlüklerden alınan
birebir satırları çalıştırır ve bir ayrıştırıcı değişikliğinin bir şeyi bozup bozmadığını görmenin
en hızlı yoludur.

## Sunucu kuralları hakkında not

Bu araç yalnızca oyunun kendisinin oluşturduğu bir günlük dosyasını okur. Yine de özel sunucuların
üçüncü taraf yazılım ve eklentilerle ilgili kendi kuralları vardır — kullanmadan önce
OriginAion'ınkilere bir göz atmakta fayda var.
