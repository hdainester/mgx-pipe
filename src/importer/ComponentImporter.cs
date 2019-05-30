using Microsoft.Xna.Framework.Content.Pipeline;

using System.Collections;
using System.Reflection;
using System.Xml;

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

            var obj = base.Import(templateFile, context);
            ParseAttributes(root, obj);
            // File.Delete(templateFile);
            return obj;
        }

        /// <summary>
        /// Parse attributes from the xml tree and assigns
        /// the values to the object. Moves down recursively
        /// therefore the object member types and order
        /// must exactly match the structure of the tree.
        /// </summary>
        /// <param name="node">Root node of the xml document</param>
        /// <param name="obj">Object to assign members on</param>
        internal static void ParseAttributes(XmlNode node, object obj) {
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
                if(col != null) next =
                    col.Count == 0 ? null : col[index++];

                ParseAttributes(child, next);
            }
        }
    }
}