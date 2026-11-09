using System.Collections.Generic;
using System.Text;

namespace Badminton.Tour
{
    /// <summary>
    /// Sinh tên VĐV HƯ CẤU theo phong cách tên từng khu vực (ghép họ + tên/âm tiết ngẫu nhiên có seed).
    /// Có danh sách chặn tên VĐV nổi tiếng có thật: nếu tình cờ ghép trùng thì sinh lại.
    /// Chỉ dùng ký tự có trong font đã bake (ASCII + tiếng Việt) – không dùng æ ø å ö ü…
    /// </summary>
    public static class NameGenerator
    {
        public struct GeneratedName { public string Full, Family; }

        // Tên VĐV có thật (viết liền, không dấu, chữ thường) – chặn cả hai thứ tự họ/tên.
        static readonly HashSet<string> Blacklist = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "lindan", "leechongwei", "chenlong", "viktoraxelsen", "kentomomota", "carolinamarin", "taitzuying",
            "pvsindhu", "sainanehwal", "taufikhidayat", "petergade", "anseyoung", "akaneyamaguchi", "chenyufei",
            "ratchanokintanon", "nguyentienminh", "leeziijia", "anthonyginting", "jonatanchristie", "kunlavutvitidsarn",
            "shiyuqi", "lohkeanyew", "andersantonsen", "lakshyasen", "kidambisrikanth", "nozomiokuhara", "hebingjiao",
            "wangzhiyi", "tanvisharma", "pranavchopra", "kentanishimoto", "wangyilyu", "zhengsiwei", "huangyaqiong",
            "chenqingchen", "jiayifan", "kevinsanjaya", "marcusgideon", "mohammadahsan", "hendrasetiawan", "seoseungjae",
            "kangminhyuk", "yutawatanabe", "arisahigashino", "hiroyukiendo", "yugokobayashi", "takurohoki",
            "liuyuchen", "liyuhui", "wangtingge", "kimastrup", "mathiasboe", "carstenmogensen", "matsfriis",
            "christinapedersen", "kamillarytterjuhl", "gabrielaadcock", "chrisadcock", "marcuselllis", "laurensmith",
            "minatsunaga", "ayakatakahashi", "misakimatsutomo", "yukifukushima", "sayakahirota", "leeyongdae",
            "yooyeonseong", "parkjoobong", "kimdongmoon", "gilyoungah", "rabanovak", "sonwanho", "jeonhyeokjin",
            "chouTienchen", "wangtzuwei", "nguyenthuylinh", "vuthitrang", "tranthiphuongthuy", "busanan",
            "pornpawee", "supanidakatethong", "kanta", "lohkeanhean", "yeojiamin", "teeJinghwa".ToLowerInvariant(),
            "ongyewsin", "teoeeyi", "aaronchia", "sohwooyik", "goh v shem".Replace(" ", ""), "tanweekiong",
            "chanpengsoon", "gohliuying", "pearlytan", "thinaahmuralitharan", "satwiksairajrankireddy", "chiragshetty",
            "ashwiniponnappa", "sikkireddy", "treesajolly", "gayatrigopichand", "hsprannoy", "parupallikashyap",
            "sameerverma", "sourabhverma", "priyanshurajawat", "kiranGeorge".ToLowerInvariant(), "mithunmanjunath",
        };

        /// <summary>Bỏ dấu, khoảng trắng, gạch nối; chữ thường – để so khớp danh sách chặn.</summary>
        static string Norm(string s)
        {
            var sb = new StringBuilder();
            foreach (char ch in s.ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD))
                if (ch >= 'a' && ch <= 'z') sb.Append(ch);
            return sb.ToString();
        }

        public static bool IsBlacklisted(string given, string family)
        {
            string a = Norm(given + family), b = Norm(family + given);
            return Blacklist.Contains(a) || Blacklist.Contains(b);
        }

        public static GeneratedName Generate(NameStyle style, Gender g, DetRandom rng, HashSet<string> used)
        {
            for (int attempt = 0; attempt < 60; attempt++)
            {
                var n = Raw(style, g, rng, out string given, out string family);
                if (IsBlacklisted(given, family)) continue;
                if (used != null && used.Contains(n.Full)) continue;
                used?.Add(n.Full);
                return n;
            }
            var f = Raw(style, g, rng, out _, out _);
            used?.Add(f.Full);
            return f;
        }

        static string P(DetRandom r, string[] a) => a[r.Next(a.Length)];

        static GeneratedName Raw(NameStyle style, Gender g, DetRandom r, out string given, out string family)
        {
            bool m = g == Gender.Male;
            switch (style)
            {
                case NameStyle.Chinese:
                {
                    family = P(r, CnFamily);
                    string s1 = P(r, m ? CnMale : CnFemale);
                    given = r.Chance(0.75) ? s1 + P(r, m ? CnMale : CnFemale).ToLowerInvariant() : s1;
                    return FamilyFirst(given, family);
                }
                case NameStyle.SingaporeChinese:
                {
                    family = P(r, HokkienFamily);
                    given = P(r, m ? CnMale : CnFemale) + " " + P(r, m ? CnMale : CnFemale);
                    return FamilyFirst(given, family);
                }
                case NameStyle.Japanese:
                    family = P(r, JpFamily); given = P(r, m ? JpMale : JpFemale);
                    return GivenFirst(given, family);
                case NameStyle.Korean:
                    family = P(r, KrFamily);
                    given = P(r, m ? KrMale1 : KrFemale1) + "-" + P(r, m ? KrMale2 : KrFemale2);
                    return FamilyFirst(given, family);
                case NameStyle.Indonesian:
                    given = P(r, m ? IdMale : IdFemale); family = P(r, m ? IdMaleFamily : IdFemaleFamily);
                    return GivenFirst(given, family);
                case NameStyle.Malaysian:
                    if (r.Chance(0.5))
                    {
                        family = P(r, HokkienFamily);
                        given = P(r, m ? CnMale : CnFemale) + " " + P(r, m ? CnMale : CnFemale);
                        return FamilyFirst(given, family);
                    }
                    given = P(r, m ? MyMale : MyFemale); family = P(r, MyFamily);
                    return GivenFirst(given, family);
                case NameStyle.Thai:
                    given = P(r, m ? ThMale : ThFemale);
                    family = P(r, ThPre) + P(r, ThMid) + P(r, ThEnd);
                    return GivenFirst(given, family);
                case NameStyle.Vietnamese:
                {
                    family = P(r, VnFamily);
                    given = P(r, m ? VnMaleMid : VnFemaleMid) + " " + P(r, m ? VnMale : VnFemale);
                    return FamilyFirst(given, family);
                }
                case NameStyle.Indian:
                    given = P(r, m ? InMale : InFemale); family = P(r, InFamily);
                    return GivenFirst(given, family);
                case NameStyle.Nordic:
                    given = P(r, m ? NoMale : NoFemale); family = P(r, NoPre) + P(r, NoSuf);
                    return GivenFirst(given, family);
                case NameStyle.French:
                    given = P(r, m ? FrMale : FrFemale); family = P(r, FrFamily);
                    return GivenFirst(given, family);
                case NameStyle.German:
                    given = P(r, m ? DeMale : DeFemale); family = P(r, DeFamily);
                    return GivenFirst(given, family);
                case NameStyle.British:
                    given = P(r, m ? GbMale : GbFemale); family = P(r, GbFamily);
                    return GivenFirst(given, family);
                case NameStyle.Spanish:
                    given = P(r, m ? EsMale : EsFemale); family = P(r, EsFamily);
                    return GivenFirst(given, family);
                case NameStyle.Dutch:
                    given = P(r, m ? NlMale : NlFemale); family = P(r, NlFamily);
                    return GivenFirst(given, family);
                default:
                    given = P(r, m ? UsMale : UsFemale); family = P(r, UsFamily);
                    return GivenFirst(given, family);
            }
        }

        static GeneratedName FamilyFirst(string given, string family) => new GeneratedName { Full = family + " " + given, Family = family };
        static GeneratedName GivenFirst(string given, string family) => new GeneratedName { Full = given + " " + family, Family = family };

        // ───────────────────────────── Bảng tên ─────────────────────────────
        static readonly string[] CnFamily = { "Wang", "Zhang", "Liu", "Yang", "Zhao", "Huang", "Zhou", "Wu", "Xu", "Sun", "Ma", "Zhu", "Hu", "Guo", "Luo", "Gao", "Liang", "Song", "Tang", "Han", "Feng", "Deng", "Cao", "Peng", "Zeng", "Xiao", "Tian", "Dong", "Pan", "Yuan", "Cai", "Jiang", "Du", "Ye", "Cheng", "Wei", "Su", "Lu", "Ding", "Ren" };
        static readonly string[] CnMale = { "Hao", "Jun", "Tao", "Lei", "Peng", "Xuan", "Zhe", "Kai", "Rui", "Bo", "Jie", "Ming", "Qiang", "Feng", "Hang", "Zi", "Han", "Xin", "Cheng", "Yi", "Ze", "Ran", "Tian", "Hong", "Shen", "Kang" };
        static readonly string[] CnFemale = { "Mei", "Xue", "Yan", "Lan", "Jing", "Hui", "Qing", "Ling", "Yue", "Ya", "Fang", "Ting", "Wan", "Shu", "Rou", "Lu", "Yun", "Zhen", "Na", "Xiao", "Qian", "Si", "Jia", "Wen", "Yu", "Xi" };
        static readonly string[] HokkienFamily = { "Tan", "Ng", "Ong", "Chua", "Teo", "Yap", "Koh", "Chong", "Low", "Foo", "Lau", "Quek", "Seah", "Toh", "Ang", "Kwek", "Heng", "Sim", "Pang", "Liew" };
        static readonly string[] JpFamily = { "Sato", "Suzuki", "Takahashi", "Tanaka", "Ito", "Nakamura", "Kato", "Yoshida", "Yamada", "Sasaki", "Inoue", "Kimura", "Hayashi", "Shimizu", "Yamazaki", "Mori", "Ikeda", "Hashimoto", "Ishikawa", "Ogawa", "Okada", "Fujita", "Goto", "Hasegawa", "Murakami", "Kondo", "Ishii", "Sakamoto", "Aoki", "Fujii", "Nishimura", "Fukuda", "Ota", "Miura", "Takeuchi", "Nakano", "Kudo", "Arai", "Noguchi", "Sugimoto" };
        static readonly string[] JpMale = { "Haruto", "Sota", "Yuto", "Riku", "Kaito", "Ren", "Daiki", "Takumi", "Shota", "Kenji", "Ryota", "Yuma", "Hayato", "Kosei", "Tomoya", "Naoki", "Sora", "Itsuki", "Minato", "Asahi" };
        static readonly string[] JpFemale = { "Yui", "Hina", "Aoi", "Sakura", "Rin", "Mio", "Yuna", "Saki", "Mai", "Honoka", "Nanami", "Ayaka", "Kana", "Riko", "Haruka", "Tsumugi", "Kaede", "Mitsuki", "Akari", "Chihiro" };
        static readonly string[] KrFamily = { "Kim", "Lee", "Park", "Choi", "Jung", "Cho", "Yoon", "Jang", "Lim", "Han", "Oh", "Shin", "Kwon", "Hwang", "Ahn", "Song", "Hong", "Ko", "Moon", "Yang", "Bae", "Baek", "Heo", "Nam", "Noh", "Ryu", "Woo", "Chun" };
        static readonly string[] KrMale1 = { "Min", "Ji", "Do", "Ha", "Joon", "Hyun", "Woo", "Sung", "Tae", "Jae", "Dong", "Young", "Sang", "Gun", "Eun" };
        static readonly string[] KrMale2 = { "jun", "ho", "woo", "hyun", "min", "seok", "hoon", "jin", "soo", "bin", "yeol", "won" };
        static readonly string[] KrFemale1 = { "Seo", "Ji", "Ha", "Yu", "Su", "Min", "Chae", "Ye", "Da", "Na", "Eun", "Hye", "So", "Ga" };
        static readonly string[] KrFemale2 = { "yeon", "eun", "min", "won", "jin", "ah", "rin", "bin", "hee", "young", "kyung", "ri" };
        static readonly string[] IdMale = { "Rizky", "Bagus", "Dimas", "Fajar", "Arif", "Yoga", "Eko", "Galih", "Ilham", "Aditya", "Bayu", "Reza", "Wahyu", "Gilang", "Teguh", "Andika" };
        static readonly string[] IdMaleFamily = { "Pratama", "Saputra", "Wijaya", "Nugroho", "Kurniawan", "Santoso", "Ramadhan", "Firmansyah", "Hakim", "Purnomo", "Gunawan", "Prasetyo", "Halim", "Wibowo" };
        static readonly string[] IdFemale = { "Putri", "Ayu", "Dewi", "Sari", "Fitri", "Indah", "Rani", "Wulan", "Nadia", "Melati", "Anisa", "Citra", "Laras", "Intan" };
        static readonly string[] IdFemaleFamily = { "Lestari", "Rahayu", "Kusuma", "Maharani", "Permata", "Wulandari", "Anggraini", "Pertiwi", "Safitri", "Puspita", "Handayani", "Ramadhani" };
        static readonly string[] MyMale = { "Aiman", "Haziq", "Irfan", "Danial", "Hakim", "Amir", "Faris", "Syahmi", "Izzat", "Afiq", "Hafiz", "Naim", "Zikri", "Luqman" };
        static readonly string[] MyFemale = { "Aisyah", "Hajar", "Farah", "Aina", "Syafiqah", "Balqis", "Damia", "Alya", "Qistina", "Sofea", "Insyirah", "Hani" };
        static readonly string[] MyFamily = { "Rosli", "Ismail", "Zulkifli", "Rahman", "Aziz", "Kamal", "Salleh", "Yusof", "Hamdan", "Roslan", "Fauzi", "Shukri", "Jamil", "Nordin" };
        static readonly string[] ThMale = { "Kittipong", "Natthawut", "Sarawut", "Thanakorn", "Pakorn", "Wichai", "Anurak", "Chaiwat", "Teerapat", "Phuwanat", "Siwakorn", "Nattapong", "Krit", "Thanawat" };
        static readonly string[] ThFemale = { "Siriporn", "Kanokwan", "Ratchada", "Nattaya", "Wanida", "Jiraporn", "Pornpan", "Chanida", "Kulnida", "Thitirat", "Waraporn", "Sasithorn", "Pimpaka" };
        static readonly string[] ThPre = { "Sri", "Kaew", "Bun", "Chai", "Thong", "Suk", "Wong", "Phon", "Sae", "Inta", "Sutha", "Kham", "Rat", "Mee" };
        static readonly string[] ThMid = { "sa", "wa", "na", "ra", "pha", "ya", "thi", "", "", "ka" };
        static readonly string[] ThEnd = { "sawat", "wong", "chai", "phan", "kul", "siri", "porn", "chart", "det", "nont", "rak", "sombat" };
        static readonly string[] VnFamily = { "Nguyễn", "Trần", "Lê", "Phạm", "Hoàng", "Huỳnh", "Phan", "Vũ", "Võ", "Đặng", "Bùi", "Đỗ", "Hồ", "Ngô", "Dương", "Lý", "Trương", "Mai" };
        static readonly string[] VnMaleMid = { "Văn", "Đức", "Minh", "Quốc", "Thành", "Hữu", "Gia", "Hoàng", "Công", "Bảo" };
        static readonly string[] VnMale = { "Khoa", "Huy", "Phúc", "Bảo", "Long", "Nam", "Tùng", "Khang", "Hiếu", "Dũng", "Quân", "Thịnh", "Đạt", "Sơn", "Vinh", "Lộc" };
        static readonly string[] VnFemaleMid = { "Thị", "Ngọc", "Thu", "Bảo", "Khánh", "Minh", "Phương", "Hải", "Mỹ" };
        static readonly string[] VnFemale = { "Trang", "Vy", "Hà", "Anh", "Ngân", "Thảo", "My", "Châu", "Yến", "Hân", "Nhi", "Quỳnh", "Tâm", "Diệp" };
        static readonly string[] InMale = { "Arjun", "Rohan", "Aditya", "Karan", "Vikram", "Rahul", "Siddharth", "Nikhil", "Varun", "Akash", "Ishaan", "Dev", "Kabir", "Yash", "Aarav" };
        static readonly string[] InFemale = { "Ananya", "Priya", "Kavya", "Meera", "Diya", "Riya", "Isha", "Neha", "Shreya", "Pooja", "Aarohi", "Ira", "Sanya", "Myra" };
        static readonly string[] InFamily = { "Verma", "Reddy", "Nair", "Iyer", "Menon", "Rao", "Patil", "Desai", "Joshi", "Kulkarni", "Pillai", "Malhotra", "Mehta", "Bhatia", "Saxena", "Kapoor", "Banerjee", "Gupta" };
        static readonly string[] NoMale = { "Mads", "Rasmus", "Magnus", "Frederik", "Emil", "Mikkel", "Jonas", "Nikolaj", "Kasper", "Tobias", "Anton", "Asger", "Villads", "Sander" };
        static readonly string[] NoFemale = { "Freja", "Ida", "Sofie", "Emma", "Clara", "Maja", "Signe", "Astrid", "Nanna", "Julie", "Laura", "Karla", "Alberte", "Liva" };
        static readonly string[] NoPre = { "Holm", "Lund", "Berg", "Skov", "Dal", "Vest", "Kjaer", "Ros", "Lind", "Strand", "Bro", "Fjeld", "Hav", "Sol", "Eng", "Mose" };
        static readonly string[] NoSuf = { "gaard", "strup", "by", "holt", "mark", "borg", "baek", "lev", "skov", "vig", "rup" };
        static readonly string[] FrMale = { "Lucas", "Hugo", "Théo", "Louis", "Arthur", "Jules", "Maxime", "Antoine", "Bastien", "Mathis" };
        static readonly string[] FrFemale = { "Léa", "Chloé", "Manon", "Camille", "Inès", "Jade", "Louise", "Zoé", "Margaux", "Elise" };
        static readonly string[] FrFamily = { "Moreau", "Garnier", "Fournier", "Girard", "Bonnet", "Dupuis", "Lambert", "Fontaine", "Rousseau", "Mercier", "Blanchard", "Chevalier", "Perrin", "Gauthier" };
        static readonly string[] DeMale = { "Lukas", "Jonas", "Felix", "Maximilian", "Paul", "Niklas", "Tim", "Jan", "Leon", "Moritz" };
        static readonly string[] DeFemale = { "Lena", "Hannah", "Lea", "Anna", "Mia", "Lara", "Johanna", "Marie", "Greta", "Paula" };
        static readonly string[] DeFamily = { "Becker", "Hoffmann", "Schulz", "Wagner", "Koch", "Richter", "Wolf", "Neumann", "Schwarz", "Krause", "Brandt", "Vogel", "Lehmann", "Seidel" };
        static readonly string[] GbMale = { "Oliver", "Harry", "George", "Jack", "Charlie", "Thomas", "James", "Alfie", "Freddie", "Archie" };
        static readonly string[] GbFemale = { "Amelia", "Olivia", "Isla", "Ava", "Emily", "Grace", "Poppy", "Ella", "Imogen", "Florence" };
        static readonly string[] GbFamily = { "Whitmore", "Ashford", "Pembroke", "Hartley", "Barker", "Fletcher", "Thornton", "Holloway", "Kendrick", "Blackwood", "Langley", "Pryce" };
        static readonly string[] EsMale = { "Pablo", "Álvaro", "Diego", "Sergio", "Javier", "Marcos", "Hugo", "Adrián" };
        static readonly string[] EsFemale = { "Lucía", "Paula", "Alba", "Marta", "Irene", "Noelia", "Elena", "Nerea" };
        static readonly string[] EsFamily = { "Navarro", "Moreno", "Romero", "Ortega", "Delgado", "Castillo", "Rubio", "Iglesias", "Molina", "Serrano" };
        static readonly string[] NlMale = { "Daan", "Sem", "Lars", "Bram", "Thijs", "Jesse", "Ruben", "Stijn" };
        static readonly string[] NlFemale = { "Sanne", "Fleur", "Lotte", "Femke", "Noor", "Iris", "Anouk", "Roos" };
        static readonly string[] NlFamily = { "de Vries", "van Dijk", "Bakker", "Visser", "Smit", "Meijer", "de Boer", "Mulder", "Dekker", "van Leeuwen" };
        static readonly string[] UsMale = { "Ethan", "Mason", "Logan", "Ryan", "Tyler", "Nathan", "Caleb", "Owen", "Hunter", "Dylan" };
        static readonly string[] UsFemale = { "Madison", "Chloe", "Hailey", "Brooke", "Avery", "Paige", "Riley", "Sydney", "Kayla", "Morgan" };
        static readonly string[] UsFamily = { "Carter", "Brooks", "Sullivan", "Hayes", "Bennett", "Parker", "Reed", "Foster", "Coleman", "Dalton", "Mercer", "Whitfield" };
    }
}
