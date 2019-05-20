using Microsoft.Xna.Framework.Content.Pipeline.Serialization.Intermediate;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using System.Linq;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using Microsoft.Xna.Framework.Content;
using Chaotx.Mgx;

namespace Chaotx.Mgx.Pipeline {
    delegate int XmlNodeComparer(XmlNode n1, XmlNode n2);

    public static class XmlHelper {
        internal static XmlNodeComparer DefaultComparer;
        internal static HashSet<XmlNode> NewNodes;
        internal static string ContentRoot {get; set;}

        static XmlHelper() {
            // By default nodes are compared by their name
            DefaultComparer = (n1, n2) => n1.Name.CompareTo(n2.Name);
            NewNodes = new HashSet<XmlNode>();
        }

        /// <summary>
        /// Converts the given relative path to the
        /// absolute file path dependent of the root
        /// from the ContentImporterContext.
        /// </summary>
        /// <param name="relativePath"></param>
        /// <returns>Absolute path</returns>
        internal static string ResolvePath(string relativePath) {
            return (ContentRoot == null ? relativePath
                : ContentRoot + "/" + relativePath) + ".mgxml";
        }

        /// <summary>
        /// Parses the given file, which is expected
        /// to be in mgxml format, to the native xml
        /// format from Xna.
        /// </summary>
        /// <param name="filename"></param>
        /// <returns>Path to native file</returns>
        public static string ParseToNativeXml(string filename) {
            string raw = File.ReadAllText(filename);
            string outfile = filename + ".native";
            string parsed = ParseRaw(raw);
            File.WriteAllText(outfile, parsed);
            return outfile;
        }

        /// <summary>
        /// Performs raw text replacement on the passed
        /// raw mgxml string and returns it in native format.
        /// </summary>
        /// <param name="raw">Raw mgxml text</param>
        /// <returns>Native xml text</returns>
        internal static string ParseRaw(string raw) {
            raw = Regex.Replace(raw, @"<(\w+)(\s+\w[\w\s=/'""\.]*?)\s*/>",
                match => match.Result(@"<$1$2></$1>"));

            raw = Regex.Replace(raw, @"<(\w+)Asset(\s+\w[\w\s=/'""\.]*)?>",
                match => match.Result(@"<$1Asset><Properties$2>"));

            string pattern =
                @"<(\w+)Asset\s*>\s*<Properties"
                + @"(\s+\w[\w\s=/'""\.]*?)?\s*"
                + @"(Template\s*=\s*""[\w\s/\.]*?"")"
                + @"(\s*)(\w[\w\s=/'""\.]*?\s*)?>";

            raw = Regex.Replace(raw, pattern, match =>
                match.Result(@"<$1Asset $3><Properties$2$4$5>"));

            return Regex.Replace(raw, @"</(\w+)Asset\s*>", match =>
                match.Result("</Properties></$1Asset>"));
        }

        /// <summary>
        /// Scans the document for Template attributes
        /// and inserts the content of the referenced
        /// files recursivley. 
        /// </summary>
        /// <param name="node">Root node of the xml document</param>
        /// <param name="contentRoot">Path to content folder</param>
        public static void ResolveTemplates(this XmlNode node, string contentRoot = null) {
            if(ContentRoot == null) ContentRoot = contentRoot;
            string attValue = null;

            if((attValue = node.GetAttributeValue("Template")) != null) {
                // load template document
                XmlDocument doc = new XmlDocument();
                doc.Load(ResolvePath(attValue));
                var root = doc.SelectSingleNode("/XnaContent/Asset");

                // add attributes that are not already present
                if(root.Attributes != null) {
                    foreach(XmlAttribute att in root.Attributes)
                        if(node.GetAttributeValue(att.Name) == null) {
                            var clone = node.OwnerDocument.CreateAttribute(att.Name);
                            clone.Value = att.Value;
                            node.Attributes.Append(clone);
                        }
                }

                // because of this line full type name is required in xml (TODO)
                var type = Type.GetType(root.GetAttributeValue("Type") + ", mgx", true);

                // a custom comparer is used comparing the
                // members mapped by the node names
                XmlNodeComparer comparer = (n1, n2) => {
                    var mem1 = ImportHelper.GetAssignedMember(n1.Name, type);
                    var mem2 = ImportHelper.GetAssignedMember(n2.Name, type);
                    if(mem1.DeclaringType.IsSubclassOf(mem2.DeclaringType)) return 1;
                    if(mem2.DeclaringType.IsSubclassOf(mem1.DeclaringType)) return -1;

                    var att1 = mem1.GetCustomAttribute(
                        typeof(OrderedAttribute), true) as OrderedAttribute;

                    var att2 = mem2.GetCustomAttribute(
                        typeof(OrderedAttribute), true) as OrderedAttribute;

                    return att1 != null && att2 != null
                        ? att1.Order.CompareTo(att2.Order)
                        : DefaultComparer(n1, n2);
                };

                foreach(XmlNode child in root.ChildNodes) {
                    var probe = GetChildNode(node, child.Name);
                    var mtype = ImportHelper.GetAssignedMember(child.Name, type);
                    var ptype = mtype as PropertyInfo;
                    var ftype = mtype as FieldInfo;

                    // TODO throws exception for method members
                    if(probe == null || typeof(IList).IsAssignableFrom(
                    ptype != null ? ptype.PropertyType : ftype.FieldType)) {
                        var last = node.LastChild;
                        var imp = node.AppendChild(node.OwnerDocument
                            .ImportNode(child, true));

                        NewNodes.Add(imp);
                        if(last == null)
                            node.AppendChild(imp);
                        else last.AddSibling(imp, comparer);
                    }
                }

                NewNodes.Clear();
            }

            // go down recursively
            foreach(XmlNode child in node.ChildNodes)
                ResolveTemplates(child);
        }

        /// <summary>
        /// Adds a siblin next (before or after)
        /// this node.
        /// </summary>
        /// <param name="node">Node the sibling is added to next</param>
        /// <param name="sib">Node to add as new sibbling</param>
        /// <param name="comparer">Comparer function</param>
        /// <param name="rLeft">Recursive left</param>
        /// <param name="rRight">Recursive right</param>
        internal static void AddSibling(this XmlNode node, XmlNode sib,
        XmlNodeComparer comparer = null, bool rLeft = true, bool rRight = true) {
            if(node == sib) return; // assumed to be alreay in correct spot
            if(comparer == null) comparer = DefaultComparer;
            int c = comparer(sib, node);
            if(c == 0 && !node.IsNew()) c = -1;

            if(c < 0) {
                if(!rLeft || node.PreviousSibling == null)
                    node.ParentNode.InsertBefore(sib, node);
                else node.PreviousSibling.AddSibling(sib, comparer, rLeft, false);
            } else {
                if(!rRight || node.NextSibling == null)
                    node.ParentNode.InsertAfter(sib, node);
                else node.NextSibling.AddSibling(sib, comparer, false, rRight);
            }
        }

        internal static XmlNode GetChildNode(this XmlNode node, string name) {
            foreach(XmlNode child in node.ChildNodes)
                if(child.Name.Equals(name))
                    return child;

            return null;
        }

        internal static List<XmlNode> GetChildNodes(this XmlNode node, string name = null) {
            List<XmlNode> children = new List<XmlNode>();
            foreach(XmlNode child in node.ChildNodes)
                if(name == null || child.Name.Equals(name))
                    children.Add(child);

            return children;
        }

        internal static string GetAttributeValue(this XmlNode node, string name) {
            return node.Attributes == null ? null
                : node.Attributes.GetNamedItem(name) == null ? null
                : node.Attributes.GetNamedItem(name).Value;
        }

        internal static bool IsNew(this XmlNode node) {
            return NewNodes.Contains(node);
        }

        // TODO report exception (help!)
        // static XmlNode CreateSample(Type type) {
        //     XmlDocument doc = new XmlDocument();
        //     using(XmlWriter writer = doc.CreateNavigator().AppendChild()) {
        //         var sample = Activator.CreateInstance(type);
        //         // TODO report exception: Colud not load assembly
        //         IntermediateSerializer.Serialize(writer, sample, null);
        //     }

        //     doc.Save("_sample.xml");
        //     return doc.SelectSingleNode("/XnaContent/Asset");
        // }
    }
}