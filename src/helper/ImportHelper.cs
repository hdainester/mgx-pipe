using Microsoft.Xna.Framework.Content;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Xml;
using System;

namespace Chaotx.Mgx.Pipeline {
    public static class ImportHelper {
        static Dictionary<string, Dictionary<string, MemberInfo>> MemberAlias;
        internal static readonly BindingFlags BFlags =
            BindingFlags.Instance
            | BindingFlags.Public
            | BindingFlags.NonPublic;


        static ImportHelper() {
            // Resolves property aliases from ElementName
            // and CollectionItemName attributes to true
            // PropertyInfo references
            MemberAlias = new Dictionary<string, Dictionary<string, MemberInfo>>();
        }
            
        /// <summary>
        /// Reads attributes from the xml node and
        /// assigns the values to the corresponding
        /// properties of the object.
        /// </summary>
        /// <param name="node">Xml node to read from</param>
        /// <param name="obj">Object whose properties to set</param>
        /// <param name="ign">Attributes to be ignored</param>
        internal static void AssignAttributes(XmlNode node, object obj, params string[] ign) {
            bool ignore;
            if(node.Attributes != null) {
                foreach(XmlAttribute attb in node.Attributes) {
                    ignore = false;
                    foreach(string name in ign) {
                        if(attb.Name.Equals(name)) {
                            ignore = true;
                            break;
                        }
                    }

                    if(!ignore) obj.SetMemberValue(
                        attb.Name, attb.Value);
                }
            }
        }

        /// <summary>
        /// Retrieves the member assigned to name which
        /// is by default any member found with its an
        /// ElementName or CollectionItemName equal to
        /// name.
        /// 
        /// If no such member exists the member with
        /// its identifier equal to name will be returned.
        /// </summary>
        /// <param name="name">Name of the member</param>
        /// <param name="type">Type to search in</param>
        /// <returns>Member assigned to name</returns>
        internal static MemberInfo GetAssignedMember(string name, Type type) {
            if(!MemberAlias.ContainsKey(type.FullName))
                MemberAlias.Add(type.FullName, new Dictionary<string, MemberInfo>());

            if(MemberAlias[type.FullName].ContainsKey(name))
                return MemberAlias[type.FullName][name];

            var mems = type.GetAllMembers();
            foreach(var prop in mems) {
                var atts = prop.GetCustomAttributes(
                    typeof(ContentSerializerAttribute), true) as ContentSerializerAttribute[];

                foreach(var att in atts) {
                    if(name.Equals(att.ElementName)
                    || name.Equals(att.CollectionItemName)) {
                        MemberAlias[type.FullName].Add(name, prop);
                        return prop;
                    }
                }
            }

            var _mem = type.GetAllMembers().Where(m => m.Name.Equals(name)).FirstOrDefault();
            MemberAlias[type.FullName].Add(name, _mem);
            return _mem;
        }

        internal static void SetMemberValue(this object obj, string member, object val) {
            var mem = obj.GetType().GetAllMembers()
                .Where(p => p.Name.Equals(member))
                .FirstOrDefault();

            if(mem == null) throw new XmlException(
                string.Format("No such member \"{0}\" in {1}", member, obj.GetType()));

            var prop = mem as PropertyInfo;
            var field = mem as FieldInfo;

            if(prop == null && field == null) throw new XmlException(
                string.Format("member \"{0}\" is of type function", member));

            if(prop != null && !prop.CanWrite) throw new XmlException(
                string.Format("property \"{0}\" is readonly", member));

            if(prop != null) prop.SetValue(obj, val);
            else if(field != null) field.SetValue(obj, val);
        }

        internal static object GetMemberValue(this object obj, string member) {
            var mem = obj.GetType().GetAllMembers()
                .Where(m => m.Name.Equals(member)).FirstOrDefault();

            var prop = mem as PropertyInfo;
            var field = mem as FieldInfo;

            return prop != null
                ? prop.GetValue(obj) : field != null 
                ? field.GetValue(obj) : null;
        }

        internal static HashSet<PropertyInfo> GetAllProperties(this Type type, HashSet<PropertyInfo> props = null) {
            if(props == null) props = new HashSet<PropertyInfo>();
            foreach(var prop in type.GetProperties(BFlags))
                props.Add(prop.DeclaringType.GetProperty(prop.Name, BFlags));

            if(type.BaseType == null)
                return props;

            return type.BaseType.GetAllProperties(props);
        }

        internal static HashSet<FieldInfo> GetAllFields(this Type type, HashSet<FieldInfo> fields = null) {
            if(fields == null) fields = new HashSet<FieldInfo>();
            foreach(var field in type.GetFields(BFlags))
                fields.Add(field.DeclaringType.GetField(field.Name, BFlags));

            if(type.BaseType == null)
                return fields;

            return type.BaseType.GetAllFields(fields);
        }

        internal static HashSet<MemberInfo> GetAllMembers(this Type type) {
            return new HashSet<MemberInfo>(
                type.GetAllFields().Cast<MemberInfo>()
                .Union(type.GetAllProperties()));
        }
    }
}