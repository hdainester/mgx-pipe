using Microsoft.Xna.Framework.Content.Pipeline;
using Microsoft.Xna.Framework.Content;

using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using System.Linq;
using System.Xml;
using System.IO;
using System;

using Chaotx.Mgx.Assets;
using Chaotx.Mgx.Layout;

namespace Chaotx.Mgx.Pipeline {
    [ContentImporter(".xml", ".mgxml", DisplayName = "Mgxml Importer - MonoGame Mgx", DefaultProcessor = "PassThroughProcessor")]
    public class ComponentImporter : XmlImporter {
        public override object Import(string filename, ContentImporterContext context) {
            string templateFile = filename + ".template";
            XmlDocument doc = new XmlDocument();
            doc.Load(filename);
            
            var root = doc.SelectSingleNode("/XnaContent/Asset");
            XmlHelper.ResolveTemplates(root);
            doc.Save(templateFile);

            string nativeFile = XmlHelper.ParseToNativeXml(templateFile);
            var obj = base.Import(nativeFile, context);
            ParseAttributes(root, obj);
            // File.Delete(templateFile);
            // File.Delete(nativeFile);
            return obj;
        }

        /// <summary>
        /// Parse attributes from the xml tree and assigns
        /// the values to the object. Moves down recursively
        /// therofere the object member types and order
        /// must exactly match the structure of the tree.
        /// </summary>
        /// <param name="node">Root node of the xml document</param>
        /// <param name="obj">Object to assign members on</param>
        public static void ParseAttributes(XmlNode node, object obj) {
            if(obj == null) return;
            ImportHelper.AssignAttributes(node, obj, "Type", "Template");

            int index = 0;
            string last = null;

            foreach(XmlNode child in node.ChildNodes) {
                var mem = ImportHelper.GetAssignedMember(child.Name, obj.GetType());
                var prop = mem as PropertyInfo;
                var field = mem as FieldInfo;
                var next = prop != null ? prop.GetValue(obj)
                    : field != null ? field.GetValue(obj) : null;

                if(!child.Name.Equals(last)) {
                    last = child.Name;
                    index = 0;
                }

                var col = next as IList;
                if(col != null) next = col[index++];
                var ass = next as Asset;
                if(ass != null) next = ass.RawObject;
                ParseAttributes(child, next);
            }
        }
    }
}