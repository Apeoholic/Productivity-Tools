﻿// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using System.IO;

namespace OnenoteMarkdownConverter
{
    internal sealed class Converter
    {
        #region Fields/Consts

        /// <summary>
        /// The markdown builder to use for convert
        /// </summary>
        private MarkdownBuilder _builder;

        /// <summary>
        /// Used to 
        /// </summary>
        private readonly Dictionary<String, Link> _references = new Dictionary<string, Link>();

        /// <summary>
        /// Is the conversion is currently on code preformatted segment.
        /// </summary>
        private bool _inCode;

        /// <summary>
        /// Is the conversion is currently inside list element (level for nested lists)
        /// </summary>
        private int _inListLevel;

        /// <summary>
        /// Is the conversion is currently on table
        /// </summary>
        private bool _inTable;

        /// <summary>
        /// Is the table header has been created from the first row of the table
        /// </summary>
        private bool _tableHeaderDone;

        #endregion


        /// <summary>
        /// Convert the given HTML
        /// </summary>
        /// <param name="html">the html string to convert</param>
        /// <param name="builder">the markdown builder to use</param>
        /// <returns>the markdown string</returns>
        public string ConvertHtmlToMarkdown(string html, MarkdownBuilder builder)
        {
            _inCode = false;
            _inTable = false;
            _builder = builder;
            _references.Clear();

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            if (doc.DocumentNode != null)
            {
                // traverse the HTML document
                foreach (var childNode in doc.DocumentNode.ChildNodes)
                {
                    if (childNode.NodeType != HtmlNodeType.Text)
                        TraverseNode(childNode);
                }

                // append all link references to the end
                _builder.AppendLine().AppendLine();
                foreach (var link in _references.Values)
                    builder.AppendLinkReference(link);

                // get the built markdown
                var markdown = _builder.GetMarkdown();

                // replace preformatted code whitespaces
                markdown = markdown.Replace("$-$space", " ");

                if (_builder.HtmlEncode)
                    markdown = WebUtility.HtmlEncode(markdown);

                // handle quotes that are part of the markdown
                markdown = markdown.Replace("$-$quot", "\"");

                // change empty lines to persist if required
                markdown = markdown.Replace("$-$nbsp", _builder.PersistEmptyLines ? "&nbsp;" : string.Empty);

                // fix extra lines
                markdown = Regex.Replace(markdown, "^\\s*\\n\\s*", "\n", RegexOptions.Multiline);
                markdown = Regex.Replace(markdown, "^\\s*\\n\\s*&nbsp;", "&nbsp;", RegexOptions.Multiline);
                markdown = Regex.Replace(markdown, "^&nbsp;\\s+\n#", "&nbsp;  \n#", RegexOptions.Multiline);
                //markdown = Regex.Replace(markdown, "^\\$-\\$empty_line\\s+?&nbsp;", "$-$empty_line", RegexOptions.Multiline);
                markdown = Regex.Replace(markdown, "^&nbsp;\\s+?\\$-\\$empty_line", "$-$empty_line", RegexOptions.Multiline);

                // empty lines is code should be persisted without &nbsp;
                markdown = markdown.Replace("$-$empty_line", string.Empty);

                // return
                return markdown;
            }
            else
            {
                return string.Empty;
            }
        }


        #region Private methods

        /// <summary>
        /// Traverse the HTML tree recursively from the given tree node.<br/>
        /// 1. Insert text into the markdown.<br/>
        /// 2.1. Handle element start.<br/>
        /// 2.1. Handle element child elements recursively.<br/>
        /// 2.1. Handle element end.<br/>
        /// </summary>
        private void TraverseNode(HtmlNode node)
        {
            switch (node.NodeType)
            {
                case HtmlNodeType.Text:
                    HandleText(node);
                    break;
                case HtmlNodeType.Element:
                    HandleElement(node, true);

                    foreach (var childNode in node.ChildNodes)
                        TraverseNode(childNode);

                    HandleElement(node, false);
                    break;
            }
        }

        /// <summary>
        /// Handle text node by adding it to markdown builder.<br/>
        /// Check if we have start\end of preformatted code segment.
        /// </summary>
        private void HandleText(HtmlNode node)
        {
            var text = node.InnerText;
            text = text.Replace("\r", " ").Replace("\n", " ");

            text = text.Replace("&nbsp;", _inCode ? "$-$empty_line" : "$-$nbsp");

            text = _inCode
                ? text.Replace(char.ConvertFromUtf32(160), "$-$space")
                : Regex.Replace(text, "\\s{2,}", " ");

            text = WebUtility.HtmlDecode(text);

            _builder.Append(text);

            // identifies start and end of code segment
            if (text.StartsWith("```"))
            {
                _inCode = !_inCode;
            }
        }

        /// <summary>
        /// Handle special formatting for element
        /// </summary>
        /// <param name="node">the element node</param>
        /// <param name="isOpen">true - element opening, false - element closing</param>
        private void HandleElement(HtmlNode node, bool isOpen)
        {
            HandleParagraph(node, isOpen);

            HandleLink(node, isOpen);

            HandleImage(node, isOpen);

            HandleSpan(node, isOpen);

            HandleList(node, isOpen);

            HandleTable(node, isOpen);

            HandleBr(node, isOpen);
        }

        private void HandleParagraph(HtmlNode node, bool isOpen)
        {
            if (node.Name == "p")
            {
                var header = GetHeader(node);
                if (header > 0)
                {
                    if (isOpen)
                    {
                        if (!_inTable)
                            _builder.AppendLine();
                        _builder.AppendHeader(header);
                    }
                }
                else
                {
                    if (!_inTable && !_builder.IsEndsNewLine() && isOpen)
                    {
                        _builder.AppendLine();
                    }

                    if (isOpen && _inCode)
                    {
                        var styleValue = GetStyleValue(node, "margin-left");
                        if (styleValue != null)
                        {
                            double width;
                            if (double.TryParse(styleValue.Substring(0, styleValue.Length - 3), out width))
                            {
                                for (int i = 0; i < width * 1000 / 375; i++)
                                    _builder.Append("$-$space$-$space$-$space$-$space");
                            }
                        }
                    }
                }
            }
        }

        private void HandleSpan(HtmlNode node, bool isOpen)
        {
            if (node.Name == "span")
            {
                if (!isOpen)
                    _builder.TrimEndWhitespaces();

                bool bold = CheckStylesContains(node, "font-weigh", "bold");
                bool italic = CheckStylesContains(node, "font-style", "italic");
                bool underline = CheckStylesContains(node, "text-decoration", "underline");
                bool isHeader = node.ParentNode != null && GetHeader(node.ParentNode) > 0;
                _builder.AppendDecoration(bold, italic && !isHeader, underline);

                if (!isOpen)
                    _builder.Append(" ");
            }
        }

        private void HandleLink(HtmlNode node, bool isOpen)
        {
            if (node.Name == "a")
            {
                var href = node.GetAttributeValue("href", null);
                if (href != null)
                {
                    if (isOpen)
                    {
                        _builder.Append("[");
                    }
                    else
                    {
                        int reference = AddReference(href, null);
                        _builder.Append("][").Append(reference.ToString(CultureInfo.CurrentCulture)).Append("]");
                    }
                }
            }
        }

        string imageFolder = "";
        string postFolder = "";

        private void HandleImage(HtmlNode node, bool isOpen)
        {
            if (node.Name == "img")
            {
                if(string.IsNullOrEmpty(imageFolder))
                {
                    var dialog = new System.Windows.Forms.FolderBrowserDialog();
                    dialog.Description = "Please select where you want the images to be saved";
                    System.Windows.Forms.DialogResult result = dialog.ShowDialog();
                    imageFolder = dialog.SelectedPath;
                }

                if (string.IsNullOrEmpty(postFolder))
                {
                    var dialog = new System.Windows.Forms.FolderBrowserDialog();
                    dialog.SelectedPath = imageFolder;
                    dialog.Description = "Please select where you want your post to be saved";
                    System.Windows.Forms.DialogResult result = dialog.ShowDialog();
                    postFolder = dialog.SelectedPath;
                }
                var src = node.GetAttributeValue("src", null);
                if (src != null)
                {
                    if (isOpen)
                    {
                        String name;
                        if (src.StartsWith("file:"))
                        {
                            name = Path.GetFileName(src);
                            //Save file locally
                            var image = File.ReadAllBytes(src.Replace("file:///", ""));
                            string tempname = Path.GetRandomFileName() + Path.GetExtension(name);
                            File.WriteAllBytes(imageFolder + "\\" + tempname, image);
                            //src = "data:image/png;base64," + Convert.ToBase64String(image);
                            
                            name = System.IO.Path.GetFileName(src);
                            var uri = new Uri(postFolder + "\\myfile.txt");
                            var result = uri.MakeRelativeUri(new Uri(imageFolder + "\\" + tempname)).ToString();
                            src = result;
                        }//
                        else
                        {
                           



                            var uri = new Uri(src);
                            name = uri.Segments[uri.Segments.Length - 1];
                        }

                        int reference = AddReference(src, name);
                        _builder.AppendImage(reference, name);
                    }
                }
            }
        }

        private void HandleList(HtmlNode node, bool isOpen)
        {
            if (node.Name == "ul" || node.Name == "ol")
            {
                _inListLevel += isOpen ? 1 : -1;
                if (_inListLevel <= 1)
                    _builder.AppendLine().Append("$-$empty_line");
            }

            if (node.Name == "li")
            {
                int number = 0;
                if (node.ParentNode != null && node.ParentNode.Name == "ol")
                {
                    foreach (var childNode in node.ParentNode.ChildNodes)
                    {
                        if (childNode.Name == "li")
                        {
                            number = childNode.GetAttributeValue("value", 1);
                            break;
                        }
                    }

                    for (int i = 0; i < node.ParentNode.ChildNodes.Count && node.ParentNode.ChildNodes[i] != node; i++)
                        if (node.ParentNode.ChildNodes[i].Name == "li")
                            number++;
                }

                if (isOpen)
                {
                    _builder.AppendList(_inListLevel, number);
                }
            }
        }

        private void HandleTable(HtmlNode node, bool isOpen)
        {
            if (node.Name == "table")
            {
                _inTable = isOpen;
                _tableHeaderDone = false;

                if (_inTable)
                    _builder.AppendLine().AppendLine();
            }
            else if (node.Name == "td")
            {
                if (isOpen)
                {
                    _builder.TrimEndWhitespaces().Append("|");
                }
            }
            else if (node.Name == "tr")
            {
                if (!isOpen)
                {
                    _builder.Append("|").AppendLine();
                    if (!_tableHeaderDone)
                    {
                        _tableHeaderDone = true;
                        _builder.Append("|");
                        foreach (var childNode in node.ChildNodes)
                        {
                            if (childNode.Name == "td")
                            {
                                var align = GetAlign(childNode);
                                if (align == 0)
                                    _builder.Append(":");
                                _builder.Append("---");
                                if (align >= 0)
                                    _builder.Append(":");
                                _builder.Append("|");
                            }
                        }
                        _builder.AppendLine();
                    }
                }
            }
        }

        private void HandleBr(HtmlNode node, bool isOpen)
        {
            if (node.Name == "br")
            {
                if (!isOpen)
                    _builder.AppendLine();
            }
        }

        /// <summary>
        /// Add a link reference to the references for the markdown to be generated at the end of the doc.
        /// </summary>
        /// <returns>the index of the reference</returns>
        private int AddReference(string value, string title)
        {
            Link link;
            if (!_references.TryGetValue(value, out link))
            {
                link = new Link(_references.Count + 1, value, CleanString(title));
                _references.Add(value, link);
            }
            return link.Index;
        }

        /// <summary>
        /// Clean string from OneNote added garbage.
        /// </summary>
        private static string CleanString(string str)
        {
            if (str != null)
            {
                str = str.Replace("Machine generated alternative", "");
                str = Regex.Replace(str, "&#\\d+;", "");
            }
            return str;
        }

        private static int GetAlign(HtmlNode node)
        {
            if (CheckStylesContains(node, "text-align", "center"))
            {
                return 0;
            }
            else if (CheckStylesContains(node, "text-align", "right"))
            {
                return 1;
            }
            else
            {
                foreach (var childNode in node.ChildNodes)
                {
                    if (CheckStylesContains(childNode, "text-align", "center"))
                    {
                        return 0;
                    }
                    else if (CheckStylesContains(childNode, "text-align", "right"))
                    {
                        return 1;
                    }
                }
            }
            return -1;
        }

        private static int GetHeader(HtmlNode node)
        {
            var style = node.GetAttributeValue("style", null);
            if (style != null)
            {
                int size = 0;
                var styleValues = style.Split(';');
                foreach (var styleValue in styleValues)
                {
                    if (styleValue.Contains("size"))
                    {
                        var str = Regex.Match(styleValue, @"\d+").Value;
                        size = int.Parse(str);
                    }
                }

                if (size == 16)
                    return 1;
                if (size == 14)
                    return 2;
                if (size == 12)
                {
                    if (node.ChildNodes.Count > 0 && CheckStylesContains(node.ChildNodes[0], "font-style", "italic"))
                        return 4;
                    return 3;
                }
            }
            return -1;
        }

        private static string[] GetStyles(HtmlNode node)
        {
            var style = node.GetAttributeValue("style", null);
            return style != null ? style.Split(';') : new string[0];
        }

        private static bool CheckStylesContains(HtmlNode node, string style, string data)
        {
            var styleValue = GetStyle(node, style);
            return styleValue != null && styleValue.Contains(data);
        }

        private static string GetStyle(HtmlNode node, string style)
        {
            foreach (var styleValue in GetStyles(node))
            {
                if (styleValue.Contains(style))
                    return styleValue;
            }
            return null;
        }

        private static string GetStyleValue(HtmlNode node, string style)
        {
            foreach (var styleValue in GetStyles(node))
            {
                if (styleValue.Contains(style))
                {
                    int idx = styleValue.IndexOf(':');
                    if (idx > -1 && idx < styleValue.Length - 1)
                        return styleValue.Substring(idx + 1);
                }
            }
            return null;
        }
         
        #endregion
    }
}