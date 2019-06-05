using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using System.Linq;
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
        /// Scans referenced assemblies with names beeing a
        /// member of the whitelist for a given type that
        /// matches or ends with the passed name. If the
        /// whitelist is empty all referenced assemblies
        /// will be scanned. Throws an exception if no or
        /// more than one type was found.
        /// </summary>
        /// <param name="name">Unqualified name of the type</param>
        /// <param name="whitelist">Assemblies to scan in</param>
        /// <returns>Type matching the name</returns>
        internal static Type FindType(string name, params string[] whitelist) {
            List<Type> types = new List<Type>();
            Assembly.GetCallingAssembly().GetReferencedAssemblies()
                .Where(a => whitelist.Length == 0 || whitelist.Contains(a.Name)).ToList()
                .ForEach(an => types.AddRange(Assembly
                    .Load(an).GetTypes()
                    .Where(t => t.FullName.EndsWith(name))));
            
            if(types.Count == 0) throw new ArgumentException(
                string.Format("no such type \"{0}\"", name));

            if(types.Count > 1) throw new ArgumentException(
                string.Format("\"{0}\" is ambiguous", name));

            return types[0];
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
            string tstr;

            if((attValue = node.GetAttributeValue("Template")) != null) {
                // load template document
                XmlDocument doc = new XmlDocument();
                doc.Load(contentRoot + attValue + ".mgxml");
                var root = doc.SelectSingleNode("/XnaContent/Asset");
                var type = FindType(root.GetAttributeValue("Type"), "mgx");

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
            if((tstr = node.GetAttributeValue("Type")) != null) {
                var type = FindType(tstr, "mgx");

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
    }
}