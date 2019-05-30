using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using System.Xml;
using System.IO;
using System;

namespace Chaotx.Mgx.Pipeline {
    public static class XmlHelper {
        internal static Comparison<XmlNode> DefaultComparer;

        static XmlHelper() {
            // By default nodes are compared by their name
            DefaultComparer = (n1, n2) => n1.Name.CompareTo(n2.Name);
        }

        /// <summary>
        /// Converts the given relative path to the
        /// absolute file path dependent of the root
        /// from the ContentImporterContext.
        /// </summary>
        /// <param name="relativePath"></param>
        /// <returns>Absolute path</returns>
        [Obsolete("ContentRoot was dropped")]
        internal static string ResolvePath(string relativePath) {
            return relativePath;
            // return (ContentRoot == null ? relativePath
            //     : ContentRoot + "/" + relativePath) + ".mgxml";
        }

        /// <summary>
        /// Parses the given file, which is expected
        /// to be in mgxml format, to the native xml
        /// format from Xna.
        /// </summary>
        /// <param name="filename"></param>
        /// <returns>Path to native file</returns>
        [Obsolete("No more mgxml -> xml parsing required")]
        internal static string ParseToNativeXml(string filename) {
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
        [Obsolete("No more mgxml -> xml parsing required")]
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
        /// files recursivley. All nodes are sorted
        /// using a custom comparer (see below) after the
        /// the insertion process.
        /// </summary>
        /// <param name="node">Root node of the xml document</param>
        /// <param name="contentRoot">Path to content folder</param>
        internal static void ResolveTemplates(this XmlNode node, string contentRoot = "") {
            HashSet<XmlNode> newNodes = new HashSet<XmlNode>();
            string attValue = null;

            if((attValue = node.GetAttributeValue("Template")) != null) {
                // load template document
                XmlDocument doc = new XmlDocument();
                doc.Load(contentRoot + attValue + ".mgxml");
                var root = doc.SelectSingleNode("/XnaContent/Asset");

                // add template attributes that are not already present to node
                if(root.Attributes != null) {
                    foreach(XmlAttribute att in root.Attributes) {
                        if(node.GetAttributeValue(att.Name) == null) {
                            var clone = node.OwnerDocument.CreateAttribute(att.Name);
                            clone.Value = att.Value;
                            node.Attributes.Append(clone);
                        }
                    }
                }

                // because of this line full type name is required in xml (TODO)
                var type = Type.GetType(root.GetAttributeValue("Type") + ", mgx", true);

                // add template elements that are not already present to node
                foreach(XmlNode child in root.ChildNodes)
                if(child.NodeType == XmlNodeType.Element) {
                    var probe = GetChildNode(node, child.Name);
                    var mtype = ImportHelper.GetAssignedMember(child.Name, type);
                    var ptype = mtype as PropertyInfo;
                    var ftype = mtype as FieldInfo;

                    if(ptype == null && ftype == null) throw new XmlException(
                        string.Format("no such non method member \"{0}\" in {1}", child.Name, type));

                    if(probe == null || typeof(IList).IsAssignableFrom(
                    ptype != null ? ptype.PropertyType : ftype.FieldType)) {
                        var imp = node.AppendChild(node.OwnerDocument
                            .ImportNode(child, true));

                        newNodes.Add(imp);
                        node.AppendChild(imp);
                    }
                }
            }

            // sort children of node
            string tstr;
            if((tstr = node.GetAttributeValue("Type")) != null) {
                var type = Type.GetType(tstr + ", mgx", true);
                // A custom comparer which compares by the Ordered attribute
                // of the associated properties matching the tag name of the nodes
                node.SortChildren((n1, n2) => {
                    if(n1.NodeType != XmlNodeType.Element
                    || n2.NodeType != XmlNodeType.Element)
                        return 0;
                        
                    var mem1 = ImportHelper.GetAssignedMember(n1.Name, type);
                    var mem2 = ImportHelper.GetAssignedMember(n2.Name, type);
                    
                    if(mem1 == null) throw new XmlException(
                        string.Format("no such member \"{0}\" in {1}", n1.Name, type));

                    if(mem2 == null) throw new XmlException(
                        string.Format("no such member \"{0}\" in {1}", n2.Name, type));

                    if(mem1.DeclaringType.IsSubclassOf(mem2.DeclaringType)) return 1;
                    if(mem2.DeclaringType.IsSubclassOf(mem1.DeclaringType)) return -1;

                    var att1 = mem1.GetCustomAttribute(
                        typeof(OrderedAttribute), true) as OrderedAttribute;

                    var att2 = mem2.GetCustomAttribute(
                        typeof(OrderedAttribute), true) as OrderedAttribute;

                    int c = att1 != null && att2 != null
                        ? att1.Order.CompareTo(att2.Order)
                        : DefaultComparer(n1, n2);

                    if(c == 0) {
                        if(newNodes.Contains(n1) && !newNodes.Contains(n2)) c = -1;
                        if(newNodes.Contains(n2) && !newNodes.Contains(n1)) c = 1;
                    }

                    return c;
                });
            }

            // go down recursively
            foreach(XmlNode child in node.ChildNodes) {
                ResolveTemplates(child, contentRoot);
                child.ConcatAttributeValue(node, "Id", ".", true);
            }
        }

        internal static void ConcatAttributeValue(this XmlNode target, XmlNode source, string attName, string separator = "", bool toFront = false) {
            if(source == null) return;
            XmlNode srcAtt = source.Attributes != null ? source.Attributes.GetNamedItem(attName) : null;
            XmlNode tgtAtt = target.Attributes != null ? target.Attributes.GetNamedItem(attName) : null;
            if(srcAtt != null && tgtAtt != null) {
                if(!toFront) tgtAtt.Value += separator + srcAtt.Value;
                else tgtAtt.Value = srcAtt.Value + separator + tgtAtt.Value;
            }

            target.ConcatAttributeValue(source.ParentNode, attName, ".", toFront);
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
        
        internal static void SortChildren(this XmlNode node, Comparison<XmlNode> comparer = null) {
            if(comparer == null) comparer = DefaultComparer;
            var children = node.GetChildNodes();

            children.Sort(comparer);
            children.ForEach(child => {
                node.RemoveChild(child);
                node.AppendChild(child);
            });
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