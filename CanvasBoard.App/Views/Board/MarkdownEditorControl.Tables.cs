using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CanvasBoard.App.Views.Board
{
    public partial class MarkdownEditorControl : Control
    {
        private void BuildTableGroups()
        {
            _tableGroups.Clear();
            _tableRowByLine.Clear();

            bool inCodeFence = false;
            TableGroup current = null;

            for (int li = 0; li < Document.Lines.Count; li++)
            {
                string rawLine = Document.Lines[li] ?? string.Empty;
                string trimmedStart = rawLine.TrimStart();

                bool isFenceLine = false;
                if (trimmedStart.StartsWith("```"))
                {
                    isFenceLine = true;
                    inCodeFence = !inCodeFence;
                }

                // Never treat lines inside ``` blocks as tables
                if (inCodeFence || isFenceLine)
                {
                    if (current != null)
                    {
                        FinalizeTableGroup(current);
                        current = null;
                    }
                    continue;
                }

                var parsedRow = ParseTableRow(rawLine, li);

                if (parsedRow != null)
                {
                    // This line is part of a table
                    if (current == null)
                    {
                        current = new TableGroup();
                        _tableGroups.Add(current);
                    }

                    current.Rows.Add(parsedRow);
                }
                else
                {
                    // End any current table block
                    if (current != null)
                    {
                        FinalizeTableGroup(current);
                        current = null;
                    }
                }
            }

            if (current != null)
                FinalizeTableGroup(current);

            // Build line → table row lookup
            foreach (var group in _tableGroups)
            {
                for (int ri = 0; ri < group.Rows.Count; ri++)
                {
                    var row = group.Rows[ri];
                    _tableRowByLine[row.LineIndex] = new TableRowRef
                    {
                        Group = group,
                        RowIndex = ri
                    };
                }
            }
        }

        private static TableRow ParseTableRow(string rawLine, int lineIndex)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
                return null;

            var trimmed = rawLine.Trim();

            // Must look like a canonical pipe row: start and end with '|'
            if (!trimmed.StartsWith("|") || !trimmed.EndsWith("|"))
                return null;

            // Must have at least 2 pipes
            int pipeCount = 0;
            foreach (var ch in trimmed)
                if (ch == '|') pipeCount++;
            if (pipeCount < 2)
                return null;

            // Split into interior cells:
            // "| A | B |" -> ["", " A ", " B ", ""]
            var parts = trimmed.Split('|');
            if (parts.Length < 3)
                return null;

            var cells = new List<string>();
            for (int i = 1; i < parts.Length - 1; i++)
            {
                // Keep raw cell text (including spaces and <br>)
                string tRaw = parts[i];
                cells.Add(tRaw);
            }
            
            if (cells.Count == 0)
                return null;

            // Separator detection: non-empty cells must be only '-' and ':' and include a '-'
            bool anyNonEmpty = false;
            bool allSeparatorLike = true;

            foreach (var c in cells)
            {
                var t = c.Trim();
                if (t.Length == 0)
                    continue;

                anyNonEmpty = true;

                bool hasDash = false;
                bool onlyDashOrColon = true;

                foreach (var ch in t)
                {
                    if (ch == '-')
                    {
                        hasDash = true;
                    }
                    else if (ch != ':')
                    {
                        onlyDashOrColon = false;
                        break;
                    }
                }

                if (!(onlyDashOrColon && hasDash))
                {
                    allSeparatorLike = false;
                    break;
                }
            }

            bool isSeparator = anyNonEmpty && allSeparatorLike;

            return new TableRow
            {
                LineIndex = lineIndex,
                Cells = cells,
                IsSeparator = isSeparator,
                VisualLines = 1
            };
        }

        private static readonly string[] BrSeparators = new[] { "<br>", "<br/>", "<br />" };

        private void FinalizeTableGroup(TableGroup group)
        {
            int colCount = 0;
            foreach (var row in group.Rows)
                if (row.Cells.Count > colCount)
                    colCount = row.Cells.Count;

            if (colCount == 0)
            {
                group.ColumnWidths = Array.Empty<double>();
                return;
            }

            group.ColumnWidths = new double[colCount];

            double fontSize = BaseFontSize;

            // Widths from NON-separator rows only
            foreach (var row in group.Rows)
            {
                if (row.IsSeparator)
                    continue;

                for (int c = 0; c < row.Cells.Count; c++)
                {
                    string text = row.Cells[c];

                    // Measure the widest *sub-line* split by <br>, not the raw text
                    double maxCellWidth = 0;
                    var parts = text.Split(BrSeparators, StringSplitOptions.None);
                    if (parts.Length == 0)
                        parts = new[] { string.Empty };

                    foreach (var part in parts)
                    {
                        double ww = MeasureTextWidth(part, fontSize);
                        if (ww > maxCellWidth)
                            maxCellWidth = ww;
                    }

                    double width = maxCellWidth + TableCellPaddingX * 2;

                    if (width > group.ColumnWidths[c])
                        group.ColumnWidths[c] = width;
                }
            }

            // Separator rows might be the only rows: ensure some width
            foreach (var row in group.Rows)
            {
                if (!row.IsSeparator)
                    continue;

                for (int c = 0; c < row.Cells.Count && c < group.ColumnWidths.Length; c++)
                {
                    if (group.ColumnWidths[c] <= 0)
                        group.ColumnWidths[c] = MeasureTextWidth("---", fontSize) + TableCellPaddingX * 2;
                }
            }

            // If some columns are still 0 (e.g., empty cells), assign a default width
            double defaultWidth = MeasureTextWidth("   ", fontSize) + TableCellPaddingX * 2;
            for (int c = 0; c < group.ColumnWidths.Length; c++)
            {
                if (group.ColumnWidths[c] <= 0)
                    group.ColumnWidths[c] = defaultWidth;
            }

            // Compute how many visual lines each row needs based on <br>
            foreach (var row in group.Rows)
            {
                if (row.IsSeparator)
                {
                    row.VisualLines = 1;
                    continue;
                }

                int maxLines = 1;
                foreach (var cell in row.Cells)
                {
                    var parts = cell.Split(BrSeparators, StringSplitOptions.None);
                    if (parts.Length > maxLines)
                        maxLines = parts.Length;
                }

                row.VisualLines = Math.Max(1, maxLines);
            }
        }

        // ================== TABLE DRAWING ==================

        private void DrawTableRow(DrawingContext context, TableRowRef rowRef, double y, int activeCellIndex = -1)
        {
            var group = rowRef.Group;
            var row = group.Rows[rowRef.RowIndex];

            var cells = row.Cells;
            double[] colWidths = group.ColumnWidths;

            double fontSize = BaseFontSize;
            var typeface = _baseTypeface;
            var brush = Brushes.White;

            // how many vertical slots this row needs (from <br>)
            int visualLines = row.IsSeparator ? 1 : Math.Max(row.VisualLines, 1);
            double rowHeight = BaseFontSize * LineSpacing * visualLines;

            var borderPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 120, 120, 120)), 1);
            var bg = new SolidColorBrush(Color.FromArgb(40, 80, 80, 80));

            // Pre-split cells by <br> to simulate multiline cells
            var cellLinesList = new List<string[]>(colWidths.Length);
            int maxLines = 1;

            for (int c = 0; c < colWidths.Length; c++)
            {
                string cellText = c < cells.Count ? cells[c] : string.Empty;
                var parts = cellText.Split(BrSeparators, StringSplitOptions.None);
                if (parts.Length == 0)
                    parts = new[] { string.Empty };

                cellLinesList.Add(parts);
                if (parts.Length > maxLines)
                    maxLines = parts.Length;
            }

            if (maxLines < 1)
                maxLines = 1;

            double lineHeightInCell = rowHeight / maxLines;

            double x = LeftPadding;

            for (int c = 0; c < colWidths.Length; c++)
            {
                var cellLines = cellLinesList[c];
                double cellWidth = colWidths[c];

                var rect = new Rect(x, y, cellWidth, rowHeight);
                context.FillRectangle(bg, rect);
                context.DrawRectangle(borderPen, rect);

                // Skip text for the active cell; it will be drawn as editor separately
                if (c != activeCellIndex)
                {
                    using (context.PushClip(new RoundedRect(rect)))
                    {
                        for (int li = 0; li < cellLines.Length; li++)
                        {
                            string lineText = cellLines[li];
                            var ft = new FormattedText(
                                lineText,
                                CultureInfo.CurrentCulture,
                                FlowDirection.LeftToRight,
                                typeface,
                                fontSize,
                                brush);

                            double textY = y + li * lineHeightInCell + (lineHeightInCell - ft.Height) / 2.0;
                            var textPos = new Point(x + TableCellPaddingX, textY);
                            context.DrawText(ft, textPos);
                        }
                    }
                }

                x += cellWidth;
            }
        }

        private void DrawActiveTableCellEditor(
            DrawingContext context,
            TableRowRef rowRef,
            int cellIndex,
            string cellTextRaw,
            double yTop)
        {
            var group = rowRef.Group;
            var row = group.Rows[rowRef.RowIndex];

            var parts = cellTextRaw.Split(BrSeparators, StringSplitOptions.None);
            if (parts.Length == 0)
                parts = new[] { string.Empty };

            int visualLines = Math.Max(row.VisualLines, 1);
            double rowHeight = BaseFontSize * LineSpacing * visualLines;

            int maxLines = Math.Max(visualLines, parts.Length);
            double lineHeightInCell = rowHeight / Math.Max(1, maxLines);

            double cellWidth = group.ColumnWidths[cellIndex];

            double xCellLeft = LeftPadding;
            for (int i = 0; i < cellIndex; i++)
                xCellLeft += group.ColumnWidths[i];

            double textLeft = xCellLeft + TableCellPaddingX;
            var rect = new Rect(xCellLeft, yTop, cellWidth, rowHeight);

            using (context.PushClip(new RoundedRect(rect)))
            {
                for (int li = 0; li < parts.Length; li++)
                {
                    string lineText = parts[li];
                    var ft = new FormattedText(
                        lineText,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        _baseTypeface,
                        BaseFontSize,
                        Brushes.White);

                    double textY = yTop + li * lineHeightInCell + (lineHeightInCell - ft.Height) / 2.0;
                    context.DrawText(ft, new Point(textLeft, textY));
                }
            }
        }

        // Table UI: + Row under table, + Col to the right
        private void DrawTableUi(DrawingContext context, double lineHeight)
        {
            foreach (var group in _tableGroups)
            {
                int minVisIndex = int.MaxValue;
                int maxVisIndex = -1;

                for (int i = 0; i < _visualLines.Count; i++)
                {
                    var vis = _visualLines[i];
                    if (vis.TableRef != null && ReferenceEquals(vis.TableRef.Group, group))
                    {
                        if (i < minVisIndex) minVisIndex = i;
                        if (i > maxVisIndex) maxVisIndex = i;
                    }
                }

                if (maxVisIndex < 0)
                    continue;

                double yTop = minVisIndex * lineHeight;
                double yBottom = (maxVisIndex + 1) * lineHeight;

                double xLeft = LeftPadding;
                double tableWidth = 0;
                foreach (var w in group.ColumnWidths)
                    tableWidth += w;
                double xRight = xLeft + tableWidth;

                // + Row (under last row)
                double rowBtnHeight = lineHeight * 0.7;
                var rowRect = new Rect(xLeft, yBottom + 2, tableWidth, rowBtnHeight);
                var rowBg = new SolidColorBrush(Color.FromArgb(60, 80, 160, 80));
                var rowBorder = new Pen(new SolidColorBrush(Color.FromArgb(120, 120, 200, 120)), 1);
                context.FillRectangle(rowBg, rowRect);
                context.DrawRectangle(rowBorder, rowRect);
                DrawCenteredLabel(context, rowRect, "+ Row");

                _tableHitRegions.Add(new TableHitRegion
                {
                    Rect = rowRect,
                    Group = group,
                    Type = TableHitType.AddRow
                });

                // + Col (to the right)
                double colBtnWidth = 32;
                var colRect = new Rect(xRight + 2, yTop, colBtnWidth, yBottom - yTop);
                var colBg = new SolidColorBrush(Color.FromArgb(60, 80, 80, 160));
                var colBorder = new Pen(new SolidColorBrush(Color.FromArgb(120, 120, 120, 200)), 1);
                context.FillRectangle(colBg, colRect);
                context.DrawRectangle(colBorder, colRect);
                DrawCenteredLabel(context, colRect, "+ Col");

                _tableHitRegions.Add(new TableHitRegion
                {
                    Rect = colRect,
                    Group = group,
                    Type = TableHitType.AddColumn
                });
            }
        }

        private void DrawCenteredLabel(DrawingContext context, Rect rect, string label)
        {
            var ft = new FormattedText(
                label,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                _baseTypeface,
                BaseFontSize * 0.85,
                Brushes.White);

            double x = rect.X + (rect.Width - ft.Width) / 2.0;
            double y = rect.Y + (rect.Height - ft.Height) / 2.0;
            context.DrawText(ft, new Point(x, y));
        }

        // ================== TABLE HIT / CELL FINDING ==================

        private bool TryGetTableCellFromCaret(
            int lineIndex,
            int caretCol,
            out TableRowRef rowRef,
            out int cellIndex,
            out int cellStart,
            out int cellEnd)
        {
            rowRef = null;
            cellIndex = -1;
            cellStart = 0;
            cellEnd = 0;

            if (!_tableRowByLine.TryGetValue(lineIndex, out rowRef))
                return false;

            var row = rowRef.Group.Rows[rowRef.RowIndex];
            if (row.IsSeparator)
                return false;

            string line = Document.Lines[lineIndex] ?? string.Empty;

            var pipePositions = new List<int>();
            for (int i = 0; i < line.Length; i++)
                if (line[i] == '|')
                    pipePositions.Add(i);

            if (pipePositions.Count < 2)
                return false;

            // Cells are between consecutive pipes
            for (int idx = 0; idx < pipePositions.Count - 1; idx++)
            {
                int p0 = pipePositions[idx];
                int p1 = pipePositions[idx + 1];

                int start = p0 + 1;
                int end = p1; // exclusive

                if (caretCol < start)
                {
                    // caret is left of this cell, snap to this one
                    cellIndex = idx;
                    cellStart = start;
                    cellEnd = end;
                    return true;
                }

                if (caretCol >= start && caretCol <= end)
                {
                    cellIndex = idx;
                    cellStart = start;
                    cellEnd = end;
                    return true;
                }
            }

            // Right of last cell: snap to last cell
            int lastIdx = pipePositions.Count - 2;
            cellIndex = lastIdx;
            cellStart = pipePositions[lastIdx] + 1;
            cellEnd = pipePositions[lastIdx + 1];
            return true;
        }

        private bool TryGetTableCellFromX(
            int lineIndex,
            double x,
            out TableRowRef rowRef,
            out int cellIndex,
            out int cellStart,
            out int cellEnd)
        {
            rowRef = null;
            cellIndex = -1;
            cellStart = 0;
            cellEnd = 0;

            if (!_tableRowByLine.TryGetValue(lineIndex, out rowRef))
                return false;

            var group = rowRef.Group;
            var row = group.Rows[rowRef.RowIndex];
            if (row.IsSeparator)
                return false;

            string line = Document.Lines[lineIndex] ?? string.Empty;

            // Choose column by x relative to table
            double localX = x - LeftPadding;
            if (localX < 0) localX = 0;

            int chosenCol = 0;
            double accum = 0;
            for (int c = 0; c < group.ColumnWidths.Length; c++)
            {
                double w = group.ColumnWidths[c];
                if (localX <= accum + w)
                {
                    chosenCol = c;
                    break;
                }

                accum += w;
                chosenCol = c;
            }

            // Map chosenCol to text cell boundaries
            var pipePositions = new List<int>();
            for (int i = 0; i < line.Length; i++)
                if (line[i] == '|')
                    pipePositions.Add(i);

            if (pipePositions.Count < 2)
                return false;

            if (chosenCol >= pipePositions.Count - 1)
                chosenCol = pipePositions.Count - 2;

            int p0 = pipePositions[chosenCol];
            int p1 = pipePositions[chosenCol + 1];
            cellIndex = chosenCol;
            cellStart = p0 + 1;
            cellEnd = p1;

            return true;
        }

        private double GetTableCellLeftX(TableRowRef rowRef, int cellIndex)
        {
            double x = LeftPadding;
            for (int i = 0; i < cellIndex; i++)
                x += rowRef.Group.ColumnWidths[i];

            return x + TableCellPaddingX;
        }

        private bool IsCaretAtTableCellBoundary(out string line, out int cellStart, out int cellEnd)
        {
            line = null;
            cellStart = 0;
            cellEnd = 0;

            int lineIndex = Document.CaretLine;
            int col = Document.CaretColumn;

            if (!TryGetTableCellFromCaret(lineIndex, col,
                    out _, out _, out cellStart, out cellEnd))
                return false;

            line = Document.Lines[lineIndex] ?? string.Empty;
            return true;
        }

        // ================== CARET (TABLE-AWARE) ==================

        private void DrawCaret(DrawingContext context, double lineHeight)
        {
            int caretLine = Document.CaretLine;
            int caretCol = Document.CaretColumn;

            if (caretLine < 0 || caretLine >= Document.Lines.Count)
                return;

            string lineText = Document.Lines[caretLine] ?? string.Empty;
            caretCol = Math.Clamp(caretCol, 0, lineText.Length);

            int visualIndex = 0;
            VisualLine caretVisual = null;

            for (int i = 0; i < _visualLines.Count; i++)
            {
                var v = _visualLines[i];
                if (v.DocLineIndex != caretLine)
                    continue;

                int start = v.StartColumn;
                int end = start + v.Length;

                if (caretCol >= start && caretCol <= end)
                {
                    caretVisual = v;
                    visualIndex = i;
                    break;
                }
            }

            if (caretVisual == null)
                return;

            int colInSeg = Math.Max(0, caretCol - caretVisual.StartColumn);

            double x;
            double yTop = visualIndex * lineHeight;
            double yBottom = yTop + lineHeight;

            // Special handling for table rows: caret x is inside active cell and clamped to cell width
            if (_tableRowByLine.TryGetValue(caretLine, out var rowRef) &&
                TryGetTableCellFromCaret(caretLine, caretCol,
                    out _, out var cellIndex, out var cellStart, out var cellEnd))
            {
                var row = rowRef.Group.Rows[rowRef.RowIndex];

                // Raw text of this cell
                string rawCell = lineText.Substring(cellStart, Math.Max(0, cellEnd - cellStart));
                int caretInCell = Math.Clamp(caretCol - cellStart, 0, rawCell.Length);

                const string BrToken = "<br>";

                // Figure out:
                //  - subLineIndex: which visual sub-line we're on
                //  - currentLineBeforeCaret: text from start of this sub-line to caret
                int subLineIndex = 0;
                int lastBrPos = -1;
                int scan = 0;

                while (true)
                {
                    int brIdx = rawCell.IndexOf(BrToken, scan, StringComparison.Ordinal);
                    if (brIdx < 0 || brIdx >= caretInCell)
                        break;

                    subLineIndex++;
                    lastBrPos = brIdx;
                    scan = brIdx + BrToken.Length;
                }

                string currentLineBeforeCaret;
                if (lastBrPos >= 0)
                {
                    int startOfCurrentLine = lastBrPos + BrToken.Length;
                    int len = Math.Max(0, caretInCell - startOfCurrentLine);
                    currentLineBeforeCaret = len > 0 ? rawCell.Substring(startOfCurrentLine, len) : string.Empty;
                }
                else
                {
                    currentLineBeforeCaret = caretInCell > 0 ? rawCell.Substring(0, caretInCell) : string.Empty;
                }

                // X: measure only the text on the current sub-line (no <br> spaces)
                double cellLeftX = GetTableCellLeftX(rowRef, cellIndex);
                double w = MeasureTextWidth(currentLineBeforeCaret, BaseFontSize);

                double cellWidth = rowRef.Group.ColumnWidths[cellIndex];
                double maxContentWidth = Math.Max(0, cellWidth - TableCellPaddingX * 2);
                if (w > maxContentWidth)
                    w = maxContentWidth;

                x = cellLeftX + w;

                // Y: align to the correct sub-line in this row
                int baseVisIndex = visualIndex;
                for (int i = visualIndex; i >= 0; i--)
                {
                    var v = _visualLines[i];
                    if (v.TableRef != null &&
                        ReferenceEquals(v.TableRef.Group, rowRef.Group) &&
                        v.TableRef.RowIndex == rowRef.RowIndex &&
                        v.TableVisualIndex == 0)
                    {
                        baseVisIndex = i;
                        break;
                    }
                }

                yTop = (baseVisIndex + subLineIndex) * lineHeight;
                yBottom = yTop + lineHeight;
            }
            else
            {
                string prefix = (colInSeg > 0 && caretVisual.StartColumn + colInSeg <= lineText.Length)
                    ? lineText.Substring(caretVisual.StartColumn, colInSeg)
                    : string.Empty;

                x = LeftPadding + MeasureTextWidth(prefix, BaseFontSize);
            }

            var caretPen = new Pen(Brushes.White, 1);
            context.DrawLine(caretPen, new Point(x, yTop), new Point(x, yBottom));
        }

        // ================== TABLE BUTTON HANDLERS ==================

        private void HandleTableHit(TableHitRegion hit)
        {
            switch (hit.Type)
            {
                case TableHitType.AddRow:
                    AddTableRow(hit.Group);
                    break;
                case TableHitType.AddColumn:
                    AddTableColumn(hit.Group);
                    break;
            }
        }

        // Add row at end (used by +Row button)
        private void AddTableRow(TableGroup group)
        {
            // Last non-separator row
            int lastNonSepIndex = -1;
            for (int i = 0; i < group.Rows.Count; i++)
            {
                if (!group.Rows[i].IsSeparator)
                    lastNonSepIndex = i;
            }

            if (lastNonSepIndex == -1)
                return;

            var lastRow = group.Rows[lastNonSepIndex];
            int colCount = group.ColumnWidths.Length;

            string newLine = BuildTableLine(colCount, isSeparator: false);

            int insertLineIndex = lastRow.LineIndex + 1;
            if (insertLineIndex < 0 || insertLineIndex > Document.Lines.Count)
                insertLineIndex = Document.Lines.Count;

            Document.Lines.Insert(insertLineIndex, newLine);

            // Caret inside first cell
            int caretCol = newLine.IndexOf(' ') + 1;
            if (caretCol < 0) caretCol = newLine.Length;

            Document.SetCaret(insertLineIndex, caretCol);
            _text = Document.GetText();
            InvalidateVisual();
        }

        private void AddTableRowBelowCaret(TableRowRef rowRef, int cellIndex)
        {
            int colCount = rowRef.Group.ColumnWidths.Length;
            string newLine = BuildTableLine(colCount, isSeparator: false);

            int insertLineIndex = rowRef.Group.Rows[rowRef.RowIndex].LineIndex + 1;
            if (insertLineIndex < 0 || insertLineIndex > Document.Lines.Count)
                insertLineIndex = Document.Lines.Count;

            Document.Lines.Insert(insertLineIndex, newLine);

            // Place caret in the same column (cellIndex) in the new row
            string line = newLine;
            var pipePositions = new List<int>();
            for (int i = 0; i < line.Length; i++)
                if (line[i] == '|')
                    pipePositions.Add(i);

            int caretCol;
            if (pipePositions.Count >= 2 && cellIndex >= 0 && cellIndex < pipePositions.Count - 1)
            {
                int p0 = pipePositions[cellIndex];
                int p1 = pipePositions[cellIndex + 1];
                int start = p0 + 1;

                caretCol = start;
                if (caretCol < line.Length && line[caretCol] == ' ')
                    caretCol++;
            }
            else
            {
                caretCol = newLine.IndexOf(' ') + 1;
                if (caretCol < 0) caretCol = newLine.Length;
            }

            Document.SetCaret(insertLineIndex, caretCol);
            _text = Document.GetText();
            InvalidateVisual();
        }

        private void AddTableColumn(TableGroup group)
        {
            int currentColCount = group.ColumnWidths.Length;
            int newColCount = currentColCount + 1;

            // Update rows
            foreach (var row in group.Rows)
            {
                var newCells = new List<string>(newColCount);
                newCells.AddRange(row.Cells);

                if (newCells.Count < newColCount)
                    newCells.Add(row.IsSeparator ? "---" : string.Empty);
                else
                    newCells.Add(row.IsSeparator ? "---" : string.Empty);

                string newLine = BuildTableLineFromCells(newCells);
                Document.Lines[row.LineIndex] = newLine;
            }

            // Caret into header's new column (first non-separator row)
            int headerLineIndex = -1;
            foreach (var row in group.Rows)
            {
                if (!row.IsSeparator)
                {
                    headerLineIndex = row.LineIndex;
                    break;
                }
            }

            if (headerLineIndex >= 0)
            {
                string headerLine = Document.Lines[headerLineIndex];
                int caretCol = headerLine.Length - 2;
                if (caretCol < 0) caretCol = headerLine.Length;
                Document.SetCaret(headerLineIndex, caretCol);
            }

            _text = Document.GetText();
            InvalidateVisual();
        }

        private string BuildTableLine(int colCount, bool isSeparator)
        {
            var cells = new List<string>(colCount);
            for (int i = 0; i < colCount; i++)
                cells.Add(isSeparator ? "---" : string.Empty);

            return BuildTableLineFromCells(cells);
        }

        private string BuildTableLineFromCells(List<string> cells)
        {
            var sb = new StringBuilder();
            sb.Append("|");
            for (int i = 0; i < cells.Count; i++)
            {
                if (i > 0)
                    sb.Append(" |");
                sb.Append(' ');
                sb.Append(cells[i]);
                sb.Append(' ');
            }
            sb.Append(" |");
            return sb.ToString();
        }

    }
}
