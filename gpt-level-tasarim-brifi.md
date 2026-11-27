# Oyunun gerçek kuralları ve mevcut level verisi: tasarım önerisi için brif

Sorularını cevaplamadan önce bir not: oyunda bir kaybetme kuralı var, ama dönen pistli levellerde pratikte neredeyse hiç tetiklenmiyor (ayrıntısı aşağıda). Yani "kaybetme yok" varsayımın bugünkü levellerin gerçeğine yakın. Aşağıda oyunun kodundan doğrulanmış kuralları, mevcut 10 levelin verisini ve kendi çözücümüzün ölçümlerini veriyorum. Senden genel fikir değil, doğrudan bu kurallara uygun **level şablonları ve tasarım mantığı** istiyoruz.

---

## 1. Ekran ve kurallar

Ekran üç katmandan oluşuyor:

**Üst: cam pist (döngü).** Ortasında ada boşluğu olan kapalı bir pist. Toplar şerit şerit dizilmiş halde bir "yürüyen bant" gibi saat yönünde sürekli döner. Pistin altında bir çıkış var.
- Çıkışın önünden geçen toplardan, rengi slottaki bir kutuya uyanlar banttan ayrılır ve o kutuya uçar.
- Uymayanlar tura devam eder.
- Ayrılan topun yeri bantta **boş yuva** olarak döner. Döngünün dışında bekleyen toplar (giriş kolu / kuyruk), önlerinden boş yuva geçtikçe sırayla banda katılır.
- Yani pistte "her top eninde sonunda çıkışa gelir" ama **kuyruktaki toplar ancak banttan top ayrıldıkça içeri girebilir**.
- Pistin şekli level başına özel çizilebiliyor (oval, kalp vb.). Döngüsüz pist de mümkün (U/S kanal); orada toplar sıralı kuyruk gibi çıkışa yürür ve öndeki önceliklidir.

**Orta: 5 slot.** Kutuları buraya gönderiyoruz. Slottaki kutu kendi rengindeki topları pistten toplar. Kutunun üstündeki sayı, toplaması gereken top sayısı (20–60 arası). Sayı sıfıra inince kutu patlar ve slot boşalır.

**Alt: kutu ızgarası (senin "queue" dediğin şey).** 4 ya da 5 sütun, 2 ya da 3 sıra. Şu an bütün kutular 1×1. Ama sistem L, T, 2×2 ve 3×1 gibi çok hücreli şekilleri de destekliyor.
- Oyuncu yalnızca **önü açık** (sütununun en önündeki) kutuya dokunabilir. Dokununca kutu boş bir slota gider.
- Gidenin arkasındaki kutu sütunda bir öne kayar. Sütun tamamen boşalınca görünümden kalkar, kalan sütunlar ortalanır.
- Yani oyuncunun her an **4–5 seçeneği** var (her sütunun önündeki kutu), arkadakileri görüyor ama erişemiyor.

**Sorularının cevapları:**
1. *Parça seçilince ne oluyor?* Kutu slota gider. O renkteki topların hepsini değil, **kendi sayısı kadar** topu, pistten çıkışa geldikçe toplar. Aynı renkten slotta iki kutu varsa ikisi de toplar.
2. *Parçalar seçilebilir mi?* Evet: her sütunun önündeki kutu seçilebilir. Arkadakiler sırası gelince öne kayar. Sıralı tek kuyruk değil, **4–5 paralel kuyruk**.
3. *Hedef dolunca ne oluyor?* Kutu patlar, slot boşalır. Pistin hareketi değişmez. Banttan ayrılan toplar kuyruktaki topların içeri girmesine yer açar.

**Kaybetme:** 5 slot dolu ve slottaki kutuların hiçbiri artık top alamıyorsa level kaybedilir. "LEVEL FAILED" ekranı çıkar ve level baştan başlar.

Ama dönen pistte bu neredeyse hiç olmuyor. Toplar sürekli döndüğü için bantta olan her top eninde sonunda çıkışa gelir. Slottaki her kutunun rengi de bir yerde mutlaka vardır, çünkü top sayıları kutu sayaçlarıyla birebir eşleşiyor. Teorik olarak tek kilitlenme yolu şu:
- Bant, slottaki hiçbir kutunun istemediği renklerle tamamen doludur.
- İstenen renkler ise döngünün dışındaki kuyrukta bekler.
- Banttan top ayrılmadığı için kuyruk içeri giremez.

**Gerçek oyunda gözlem:** Mevcut hareketli levellerin hepsi, kutulara rastgele basılıp biraz beklenerek bitiyor. Yani şu an oyunda gerçek bir başarısızlık baskısı yok. Oyuncunun sıralama kararları sonucu değiştirmiyor, sadece süreyi değiştiriyor.

Bu bizim için asıl tasarım sorunu: **Sıralama kararını anlamlı yapmak için ne yapmalıyız?** Bir seçenek kilitlenmeyi gerçekten mümkün kılmak (kuyruk, bant kapasitesi, döngüsüz pist). Diğer seçenek, önerdiğin gibi kaybetmeyen ama verimliliği ölçen bir sistem (yıldız, hamle, süre, kalan slot). Hangisinin ya da hangi karışımın bu oyuna uyduğunu gerekçesiyle öner.

## 2. Mekanikler (şu an var olanlar)

| Mekanik | Kural |
|---|---|
| **Dolu kutu** | İçi başka renk toplarla dolu başlar. Slota gönderilince içindekileri cama boşaltır, sonra kendi rengini toplamaya başlar. Cama yeni top ekler, yani bandı kalabalıklaştırabilir. |
| **Buz** | Donmuş kutu slota gönderilemez. Sayacı her toplanan topta 1 azalır (alternatif mod: her tamamlanan kutuda 1). Sıfırda erir. |
| **Kilit & anahtar** | Kilitli kutu, anahtar taşıyan kutular tamamlanıp anahtarlarını getirene kadar açılmaz. |
| **Ek slot ("+")** | Sistemde var: kilitli bir 6. slot açılabiliyor. Şu an level tasarımında kullanılmıyor. |

## 3. Mevcut 10 level

Kolonlar:
- **Izgara:** sütun×sıra.
- **Kutu:** ızgaradaki kutu sayısı.
- **Top:** toplam toplanacak top.
- **Seçili:** tasarımcının verdiği zorluk.
- **Puan:** çözücünün ölçtüğü 0–100.
- **Min slot:** levelın çözülebildiği en az slot sayısı.
- **İlk hamle tuzak:** açılıştaki seçeneklerden kaçının kaçınılmaz kayba götürdüğü.
- **Kritik adım:** çözüm yolunda tek doğru hamlenin olduğu adım sayısı.

| Level | Izgara | Kutu | Renk | Kutu sayaçları | Top | Mekanik | Seçili | Puan | Min slot | İlk hamle tuzak | Kritik adım |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 4×2 | 8 | 4 | 40–52 | 369 | – | Kolay | 0 | 1/5 | 0/4 | 0 |
| 2 | 4×3 | 12 | 5 | 24–34 | 354 | – | Kolay | 0 | 1/5 | 0/4 | 0 |
| 3 (sabit cam) | 4×2 | 8 | 4 | 42–54 | 379 | – | Kolay | 1 | 2/5 | 0/4 | 0 |
| 4 | 4×3 | 12 | 5 | 24–45 | 420 | dolu kutu ×1 | Orta | 0 | 1/5 | 0/4 | 0 |
| 5 | 4×3 | 12 | 6 | 24–58 | 402 | dolu kutu ×2 | Orta | 0 | 1/5 | 0/4 | 0 |
| 6 | 4×3 | 12 | 4 | 24–34 | 356 | buz ×1 | Orta | 0 | 1/5 | 0/3 | 0 |
| 7 (sabit cam) | 4×3 | 12 | 4 | 24–46 | 397 | dolu ×1, buz ×1 | Orta | 10 | 2/5 | 0/4 | 0 |
| 8 | 4×3 | 12 | 6 | 26–36 | 359 | kilit ×1, anahtar ×1 | Orta | 0 | 1/5 | 0/4 | 0 |
| 9 | 5×3 | 15 | 6 | 20–28 | 373 | buz ×1, kilit ×1, anahtar ×2 | Zor | 0 | 1/5 | 0/5 | 0 |
| 10 | 5×3 | 15 | 7 | 20–42 | 424 | dolu ×2, buz ×1, kilit ×1, anahtar ×1 | Boss | 0 | 1/5 | 0/5 | 0 |

**Ölçüm notu:** Çözücümüzü dönen pisti doğru modelleyecek şekilde düzelttik. Bant, sırası önemsiz bir renk havuzu; halka dışındaki toplar sıralı bekliyor ve bantta yer açıldıkça katılıyor. Sonuç: Hareketli 8 levelin hepsinde **bütün toplar başlangıçta halkada**, dışarıda bekleyen top yok. Bu yüzden hiçbir gönderme sırası kilitlenmiyor: puan 0, tek slotla bile çözülüyor. Oyunda gözlediğimiz de bu: rastgele basarak hepsi bitiyor. Yalnızca sabit camlı Level 3 ve 7'de küçük bir kilitlenme ihtimali var. Oyunda kilitlenmeyi mümkün kılacak araçlar zaten var ama hiçbir levelde kullanılmıyor: halka dışında kuyrukta bekleyen toplar (gizli kuyruk oranı, giriş yolu, kollar) ve bant kapasite sınırı.

**Zorluk nasıl ölçülüyor:** Senin önerdiğin ağırlıklı formül yerine simülasyon kullanıyoruz.
- Çözücü, slota gönderme hamlelerinin bütün sıralarını arıyor.
- İki bot her leveli 150'şer kez oynuyor: biri rastgele, biri "akıllı". Akıllı bot önü açık kutulardan, rengi çıkışa yakın toplarla eşleşeni seçiyor.
- Puan, akıllı botun kilitlenme oranından geliyor. Bantlar: Kolay 0–24 (akıllı ≤%5), Orta 25–49 (≤%12), Zor 50–74 (≤%50), Boss 75–100.
- Puanın yanında hata payı (min slot), ilk hamle tuzakları ve kritik adımlar da ölçülüyor.

**Gördüğümüz sorunlar:**
- Hareketli levellerde rastgele oynayarak da kazanılıyor; sıralama kararının anlamı yok.
- Yeni mekanikler peş peşe geliyor: dolu kutu L4, buz L6, kilit L8. Sonrasında hemen birleşiyorlar.
- Mekanikler öğretildikten hemen sonra başka mekanikle birleşiyor (L7, L9).
- İlk 3 level 4–5 renkle başlıyor.
- Mekanikler (buz, kilit, dolu kutu) şu an sadece bekleme süresini uzatıyor; kilitlenme olmadığı için oyuncunun kararını değiştirmiyor.

## 4. Senden istediğimiz

Bu kurallarla, aşağıdakileri **somut ve doğrudan uygulanabilir** şekilde ver:

1. **Zorluğun bu oyunda tam olarak nereden geldiğinin analizi.** Şu soruların cevabını ver:
   - Bant/kuyruk ayrımı, sütunların derinliği, kutu sayaçlarının büyüklüğü, renk sayısı, pistteki renk dağılımı (gruplu/karışık, hangi renkler kuyrukta) zorluğu ayrı ayrı nasıl etkiler?
   - Hangisi "düşündüren" zorluk, hangisi sadece "şans" ya da "kalabalık"?
2. **Karar anı yaratma teknikleri.** Örnekler:
   - Rengi henüz kuyrukta olan kutuyu öne koyma (yem kutu).
   - Aynı renkten iki kutuyu farklı derinliklere koyma.
   - Büyük sayaçlı bir kutunun slotu uzun süre meşgul etmesi.
   - Dolu kutunun banda top ekleyerek kalabalık yaratması.
   - Bunlar gibi tekniklerin her birinin oyuncuya hangi kararı aldırdığını yaz.
3. **İlk 30 level için şablon tablosu.** Her satırda:
   - Izgara (sütun×sıra) ve kutu sayısı.
   - Renk sayısı.
   - Kutu sayaç aralığı.
   - Pistteki başlangıç dağılımı (gruplu/karışık, hangi oranda kuyrukta).
   - Hangi sütunun önüne hangi tür kutu konduğu (ör. "ilk açılışta 4 seçenekten 3'ü hemen toplayabiliyor").
   - Mekanik (varsa).
   - Hedef puan bandı.
   - Hedef min slot ve ilk hamle tuzağı sayısı.
   - Levelın tek cümlelik "öğrettiği şey".
4. **Mekanik tanıtım kuralı.** Her mekaniğin:
   - Kolay bir levelde tek başına tanıtılması,
   - 1–2 level pekiştirilmesi,
   - ancak ondan sonra başka mekaniklerle birleştirilmesi.
   Dolu kutu, buz ve kilit için hangi levellerde geleceğini öner.
5. **Rahatlama ritmi.** Zirve ve rahatlama levellerinin nereye konacağını göster. Bizim ölçümlerimizdeki hedef sayılar cinsinden ver (puan, min slot).
6. **Kaybetme / başarı tasarımı.** Önce şunu karara bağla: Dönen pistte sıralama kararını anlamlı yapmak için kilitlenmeyi gerçekten mümkün mü kılalım? Yoksa kaybetmeyen bir verimlilik sistemi mi kuralım? Kilitlenme seçilirse, "oyuncu ilk dokunuşta bilmeden kaybetsin" istemiyoruz. Hangi zorlukta kaç ilk hamle tuzağına, kaç kritik adıma izin verilmeli? "+" ek slotu bir kurtarma aracı olarak nasıl kullanılabilir (ör. kaybedince bir kez teklif)?

Yeni mekanik önerebilirsin. Ama önce mevcut sistemle (bant/kuyruk, sütunlar, sayaçlar, 3 mekanik) ne kadar derinlik çıkarılabileceğini göster. Her öneriyi bizim ölçebildiğimiz sayılara bağla: puan, min slot, ilk hamle tuzağı, kritik adım. Biz de bunları level stüdyosunda doğrulayabilelim.
