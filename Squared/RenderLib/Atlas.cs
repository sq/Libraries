using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;

namespace Squared.Render {
    public class Atlas : IEnumerable<Atlas.Cell> {
        public struct Cell {
            public readonly Atlas Atlas;
            public readonly int Index;
            public readonly Bounds Bounds;
            public readonly Rectangle Rectangle;

            public Cell (Atlas atlas, int index, ref Bounds bounds, ref Rectangle rectangle) {
                Atlas = atlas;
                Index = index;
                Bounds = bounds;
                Rectangle = rectangle;
            }

            public Texture2D Texture {
                get {
                    return Atlas.Texture;
                }
            }

            public static implicit operator Texture2D (Cell cell) {
                return cell.Atlas.Texture;
            }

            public static implicit operator Rectangle (Cell cell) {
                return cell.Rectangle;
            }

            public static implicit operator Bounds (Cell cell) {
                return cell.Bounds;
            }
        }

        public struct SubRegion {
            public readonly Atlas Atlas;
            public readonly int Left, Top, Width, Height;

            public SubRegion (Atlas atlas, int left, int top, int width, int height) {
                Atlas = atlas;
                Left = left;
                Top = top;
                Width = width;
                Height = height;

                if (width <= 0)
                    throw new ArgumentOutOfRangeException("width");
                if (height <= 0)
                    throw new ArgumentOutOfRangeException("height");
            }

            public int Count {
                get {
                    return Width * Height;
                }
            }

            public Cell this[int index] {
                get {
                    int x = index % Width;
                    int y = index / Width;

                    var offsetX = Left + x;
                    var offsetY = Top + y;

                    return Atlas[offsetX, offsetY];
                }
            }

            public Cell this[int x, int y] {
                get {
                    var offsetX = Left + x;
                    var offsetY = Top + y;

                    return Atlas[offsetX, offsetY];
                }
            }
        }

        public readonly Texture2D Texture;
        public readonly int CellWidth, CellHeight;
        public readonly int MarginLeft, MarginTop, MarginRight, MarginBottom;
        public readonly int WidthInCells, HeightInCells;

        private readonly List<Cell> Cells = new List<Cell>();  

        public Atlas (
            Texture2D texture, int cellWidth, int cellHeight,
            int marginLeft = 0, int marginTop = 0,
            int marginRight = 0, int marginBottom = 0
        ) {
            Texture = texture;
            CellWidth = cellWidth;
            CellHeight = cellHeight;
            MarginLeft = marginLeft;
            MarginTop = marginTop;
            MarginRight = marginRight;
            MarginBottom = marginBottom;

            if (texture == null)
                throw new ArgumentNullException("texture");
            if (cellWidth <= 0)
                throw new ArgumentOutOfRangeException("cellWidth");
            if (cellHeight <= 0)
                throw new ArgumentOutOfRangeException("cellHeight");

            WidthInCells = InteriorWidth / CellWidth;
            HeightInCells = InteriorHeight / CellHeight;

            GenerateCells();
        }

        public static Atlas FromCount (
            Texture2D texture, int countX, int countY,
            int marginLeft = 0, int marginTop = 0,
            int marginRight = 0, int marginBottom = 0
        ) {
            var w = texture.Width - (marginLeft + marginRight);
            var h = texture.Height - (marginTop + marginTop);

            return new Atlas(
                texture, w / countX, h / countY,
                marginLeft, marginTop, marginRight, marginBottom
            );
        }

        private int InteriorWidth {
            get {
                return Texture.Width - (MarginLeft + MarginRight);
            }
        }

        private int InteriorHeight {
            get {
                return Texture.Height - (MarginTop + MarginBottom);
            }
        }

        public int Count {
            get {
                return WidthInCells * HeightInCells;
            }
        }

        public Cell this[int index] {
            get {
                return Cells[index];
            }
        }

        public Cell this[int x, int y] {
            get {
                if ((x < 0) || (x >= WidthInCells))
                    throw new ArgumentOutOfRangeException("x");
                if ((y < 0) || (y >= HeightInCells))
                    throw new ArgumentOutOfRangeException("y");

                int index = (y * WidthInCells) + x;
                return Cells[index];
            }
        }

        private void GenerateCells () {
            for (int y = 0, i = 0; y < HeightInCells; y++) {
                for (int x = 0; x < WidthInCells; x++, i++) {
                    var rectangle = new Rectangle(
                        (CellWidth * x) + MarginLeft,
                        (CellHeight * y) + MarginTop,
                        CellWidth, CellHeight
                    );
                    var bounds = Texture.BoundsFromRectangle(ref rectangle);
                    var cell = new Cell(this, i, ref bounds, ref rectangle);

                    Cells.Add(cell);
                }
            }
        }

        public List<Cell>.Enumerator GetEnumerator () {
            return Cells.GetEnumerator();
        }

        IEnumerator<Cell> IEnumerable<Cell>.GetEnumerator () {
            return Cells.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator () {
            return Cells.GetEnumerator();
        }
    }
}
