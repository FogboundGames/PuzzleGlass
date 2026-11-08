using System;
using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass
{
    public enum ParticleArrangementMode
    {
        OrganicPhysicsLayers = 0, // Klasik Huni/Dalga/Fizik Katmanları
        PixelArtVisual = 1        // 2. Görseldeki Piksel Sanatı Görsel Dizilimi (Grid)
    }

    public enum PixelArtPreset
    {
        Frog = 0,          // 🐸 Yeşil Kurbağa (Yanakları Pembe, Gülümseyen)
        Heart = 1,         // ❤️ Işıltılı Kırmızı Kalp
        Mushroom = 2,      // 🍄 Süper Mario Mantarı
        Bee = 3,           // 🐝 Çizgili Bal Arısı
        OctopusSkull = 4,  // 👾 Pembe Ahtapot / Kafatası
        Tulip = 5,         // 🌷 Mor Lale Çiçeği
        Saturn = 6,        // 🪐 Satürn Gezegeni ve Halkaları
        Penguin = 7,       // 🐧 Sevimli Penguen
        Butterfly = 8,     // 🦋 Mavi & Turkuaz Kelebek
        Custom = 9         // 🎨 Kullanıcı Tanımlı Özel Piksel Sanatı
    }

    [Serializable]
    public class PixelArtPattern
    {
        public string patternName = "Frog";
        public int width = 11;
        public int height = 8;
        public string[] rawAscii;

        public PixelArtPattern() { }

        public PixelArtPattern(string name, int w, int h, string[] ascii)
        {
            patternName = name;
            width = w;
            height = h;
            rawAscii = ascii;
        }

        /// <summary>
        /// Converts the ASCII grid into a 2D array of nullable PuzzleColor [x, y]
        /// where y=0 is the BOTTOM row and y=height-1 is the TOP row.
        /// </summary>
        public PuzzleColor?[,] GetColorGrid()
        {
            PuzzleColor?[,] grid = new PuzzleColor?[width, height];
            if (rawAscii == null || rawAscii.Length == 0) return grid;

            int asciiRows = rawAscii.Length;
            for (int r = 0; r < asciiRows && r < height; r++)
            {
                // In rawAscii line 0 is the TOP of the image
                int y = height - 1 - r;
                string line = rawAscii[r];

                for (int c = 0; c < line.Length && c < width; c++)
                {
                    grid[c, y] = CharToPuzzleColor(line[c]);
                }
            }

            return grid;
        }

        public static PuzzleColor? CharToPuzzleColor(char c)
        {
            switch (char.ToUpperInvariant(c))
            {
                case 'K': return PuzzleColor.Black;
                case 'W': return PuzzleColor.White;
                case 'R': return PuzzleColor.Red;
                case 'G': return PuzzleColor.Green;
                case 'B': return PuzzleColor.Blue;
                case 'Y': return PuzzleColor.Yellow;
                case 'P': return PuzzleColor.Pink;
                case 'V': return PuzzleColor.Purple;
                case 'O': return PuzzleColor.Orange;
                case 'C': return PuzzleColor.Cyan;
                case 'T': return PuzzleColor.Brown;
                default:  return null; // Empty / transparent cell
            }
        }

        public static char ColorToChar(PuzzleColor c)
        {
            switch (c)
            {
                case PuzzleColor.Black: return 'K';
                case PuzzleColor.White: return 'W';
                case PuzzleColor.Red: return 'R';
                case PuzzleColor.Green: return 'G';
                case PuzzleColor.Blue: return 'B';
                case PuzzleColor.Yellow: return 'Y';
                case PuzzleColor.Pink: return 'P';
                case PuzzleColor.Purple: return 'V';
                case PuzzleColor.Orange: return 'O';
                case PuzzleColor.Cyan: return 'C';
                case PuzzleColor.Brown: return 'T';
                default: return 'B';
            }
        }
    }

    /// <summary>
    /// Exact 1-to-1 Pixel Art definitions matching the 9 notebook drawings in Image 2!
    /// K=Black, W=White, R=Red, G=Green, B=Blue, Y=Yellow, P=Pink, V=Purple, C=Cyan, T=Brown/Tan, .=Empty
    /// </summary>
    public static class PixelArtLibrary
    {
        public static char ColorToChar(PuzzleColor c) => PixelArtPattern.ColorToChar(c);
        public static PixelArtPattern GetPreset(PixelArtPreset preset)
        {
            switch (preset)
            {
                case PixelArtPreset.Frog:
                    // 🐸 11x8: Yeşil kurbağa, pembe yanaklar, siyah gözler ve gülümseme
                    return new PixelArtPattern("Frog", 11, 8, new string[]
                    {
                        ".KK.....KK.",
                        "KGGK...KGGK",
                        "KGKKGGGKGGK",
                        "KGGGGGGGGGK",
                        "KPGGGGGGGKP",
                        "KGGKKKKKGGK",
                        "KGGGGGGGGGK",
                        ".KKKKKKKKK."
                    });

                case PixelArtPreset.Heart:
                    // ❤️ 9x8: Sol üstte beyaz ışıltılı pikselleri olan kırmızı kalp
                    return new PixelArtPattern("Heart", 9, 8, new string[]
                    {
                        ".KKK.KKK.",
                        "KRWWKRRRK",
                        "KRWWKRRRK",
                        "KRRRRRRRK",
                        ".KRRRRRK.",
                        "..KRRRK..",
                        "...KRK...",
                        "....K...."
                    });

                case PixelArtPreset.Mushroom:
                    // 🍄 10x10: Süper Mario mantarı (kırmızı şapka, beyaz benekler, ten sap)
                    return new PixelArtPattern("Mushroom", 10, 10, new string[]
                    {
                        "...KKKK...",
                        ".KKRRRRKK.",
                        "KRRWWRRRRK",
                        "KRRWWWRWRK",
                        "KRWRRRRRRK",
                        "KKKKKKKKKK",
                        ".KTTKKTTK.",
                        ".KTTKKTTK.",
                        ".KTTTTTTK.",
                        "..KKKKKK.."
                    });

                case PixelArtPreset.Bee:
                    // 🐝 10x9: Sarı & siyah çizgili bal arısı, beyaz kanatlar, siyah iğne ve anten
                    return new PixelArtPattern("Bee", 10, 9, new string[]
                    {
                        "...KK..KK.",
                        "..KWWKKWWK",
                        "..KKKKKKKK",
                        ".KYYYYKYYK",
                        "KKKKKKKKKK",
                        "KYYYYKYKKY",
                        ".KYYYYKYYK",
                        "..KKKKKKKK",
                        "...KK..KK."
                    });

                case PixelArtPreset.OctopusSkull:
                    // 👾 9x9: Pembe kafatası / ahtapot, siyah gözler ve bacaklar
                    return new PixelArtPattern("OctopusSkull", 9, 9, new string[]
                    {
                        "..KKKKK..",
                        ".KPPPPPK.",
                        "KPPPPPPPK",
                        "KPKKPKKPK",
                        "KPKKPKKPK",
                        "KPPPPPPPK",
                        ".KKKKKKK.",
                        "KPKPKPKPK",
                        "KK.KK.KK."
                    });

                case PixelArtPreset.Tulip:
                    // 🌷 7x11: Mor taç yapraklı lale, turkuaz gövde ve yapraklar
                    return new PixelArtPattern("Tulip", 7, 11, new string[]
                    {
                        "KV.KV.K",
                        "KVVVVVK",
                        "KVVVVVK",
                        "KVVVVVK",
                        ".KVVVK.",
                        "..KKK..",
                        "...KC..",
                        ".CKKC..",
                        "..KKCKC",
                        "...KC..",
                        "..KKKC."
                    });

                case PixelArtPreset.Saturn:
                    // 🪐 13x7: Sarı gezegen, siyah ve beyaz halkalar
                    return new PixelArtPattern("Saturn", 13, 7, new string[]
                    {
                        "....KKKKK....",
                        "..KKYYYYYKK..",
                        ".KWWYYYYYWK..",
                        "KKKKYYYYYKKKK",
                        ".KWWYYYYYWK..",
                        "..KKYYYYYKK..",
                        "....KKKKK...."
                    });

                case PixelArtPreset.Penguin:
                    // 🐧 9x10: Siyah sırt, beyaz karın, sarı gaga ve ayaklar
                    return new PixelArtPattern("Penguin", 9, 10, new string[]
                    {
                        "..KKKKK..",
                        ".KWWWWWK.",
                        "KWWWWWWWK",
                        "KWKKWKKWK",
                        "KWWWYWWK.",
                        "KWWWWWWWK",
                        "KWWWWWWWK",
                        ".KKKKKKK.",
                        ".KYYKYYK.",
                        "..KK.KK.."
                    });

                case PixelArtPreset.Butterfly:
                    // 🦋 11x11: Mavi ve turkuaz kanatlı, siyah gövdeli kelebek
                    return new PixelArtPattern("Butterfly", 11, 11, new string[]
                    {
                        "....K...K..",
                        "...KK..KK..",
                        "..KBBKKBBK.",
                        ".KBBBCCBBK.",
                        "KBBCCCCBBK.",
                        ".KBBBCCBBK.",
                        "..KBBCCBBK.",
                        ".KBBCCCCBBK",
                        "KBBCCCCBBBK",
                        ".KBBKKBBK..",
                        "..KK..KK..."
                    });

                default:
                    return GetPreset(PixelArtPreset.Frog);
            }
        }

        public static readonly string[] PresetDisplayNames = new string[]
        {
            "🐸 Yeşil Kurbağa (Görsel 2)",
            "❤️ Işıltılı Kırmızı Kalp (Görsel 2)",
            "🍄 Süper Mantar (Görsel 2)",
            "🐝 Çizgili Bal Arısı (Görsel 2)",
            "👾 Pembe Ahtapot / Kafatası (Görsel 2)",
            "🌷 Mor Lale Çiçeği (Görsel 2)",
            "🪐 Satürn Gezegeni (Görsel 2)",
            "🐧 Sevimli Penguen (Görsel 2)",
            "🦋 Mavi Kelebek (Görsel 2)",
            "🎨 Özel Piksel Sanatı (Custom)"
        };
    }
}

