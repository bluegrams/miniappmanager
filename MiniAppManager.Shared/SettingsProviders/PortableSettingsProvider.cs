﻿using System;
using System.Collections;
using System.Collections.Specialized;
using System.Configuration;
using System.Xml.Linq;
using System.IO;
using System.Reflection;
using System.Xml;

namespace Bluegrams.Application.SettingsProviders
{
    /// <summary>
    /// Provides portable, persistent application settings.
    /// </summary>
    public class PortableSettingsProvider : SettingsProvider, IApplicationSettingsProvider
    {

        private XDocument GetXmlDoc()
        {
            // to deal with multiple settings providers accessing the same file, reload on every set or get request.
            XDocument xmlDoc = null;
            bool initnew = false;
            if (File.Exists(this.ApplicationSettingsFile))
            {
                try
                {
                    xmlDoc = XDocument.Load(ApplicationSettingsFile);
                }
                catch { initnew = true; }
            }
            else
                initnew = true;
            if (initnew)
            {
                xmlDoc = new XDocument(new XElement("configuration",
                    new XElement("userSettings", new XElement("Roaming"))));
                string version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
                xmlDoc.AddFirst(new XComment("Portable settings file. Generated by MiniAppManager v." + version + "."));
            }
            return xmlDoc;
        }

        private string ApplicationSettingsFile { get => Path.Combine(Path.GetDirectoryName(AppInfo.Location), "portable.config"); }

        public override string ApplicationName { get => AppInfo.ProductName; set { } }

        public override void Initialize(string name, NameValueCollection config)
        {
            base.Initialize(this.ApplicationName, config);
        }

        public SettingsPropertyValue GetPreviousVersion(SettingsContext context, SettingsProperty property)
        {
            throw new NotImplementedException();
        }

        public void Reset(SettingsContext context)
        {
            if (File.Exists(ApplicationSettingsFile))
                File.Delete(ApplicationSettingsFile);
        }

        public void Upgrade(SettingsContext context, SettingsPropertyCollection properties)
        { /* don't do anything here*/ }

        public override SettingsPropertyValueCollection GetPropertyValues(SettingsContext context, SettingsPropertyCollection collection)
        {
            XDocument xmlDoc = GetXmlDoc();
            SettingsPropertyValueCollection values = new SettingsPropertyValueCollection();
            // iterate through settings to be retrieved
            foreach(SettingsProperty setting in collection)
            {
                SettingsPropertyValue value = new SettingsPropertyValue(setting);
                value.IsDirty = false;
                //Set serialized value to xml element from file. This will be deserialized by SettingsPropertyValue when needed.
                value.SerializedValue = getXmlValue(xmlDoc, XmlConvert.EncodeLocalName((string)context["GroupName"]), setting);
                values.Add(value);
            }
            return values;
        }

        public override void SetPropertyValues(SettingsContext context, SettingsPropertyValueCollection collection)
        {
            XDocument xmlDoc = GetXmlDoc();
            foreach (SettingsPropertyValue value in collection)
            {
                setXmlValue(xmlDoc, XmlConvert.EncodeLocalName((string)context["GroupName"]), value);
            }
            try
            {
                xmlDoc.Save(ApplicationSettingsFile);
            } catch { }
        }

        private string getXmlValue(XDocument xmlDoc, string scope, SettingsProperty prop)
        {
            string result = "";
            if (!IsUserScoped(prop))
                return result;
            //determine the location of the settings property
            XElement xmlSettings = xmlDoc.Element("configuration").Element("userSettings");
            if (IsRoaming(prop))
                xmlSettings = xmlSettings.Element("Roaming");
            else xmlSettings = xmlSettings.Element("PC_" + Environment.MachineName);
            // retrieve the value or set to default if available
            if (xmlSettings != null && xmlSettings.Element(scope) != null && xmlSettings.Element(scope).Element(prop.Name) != null)
            {
                var reader = xmlSettings.Element(scope).Element(prop.Name).CreateReader();
                reader.MoveToContent();
                result = reader.ReadInnerXml();
            }
            else if (prop.DefaultValue != null)
                result = prop.DefaultValue.ToString();
            return result;
        }

        private void setXmlValue(XDocument xmlDoc, string scope, SettingsPropertyValue value)
        { 
            if (!IsUserScoped(value.Property)) return;
            //determine the location of the settings property
            XElement xmlSettings = xmlDoc.Element("configuration").Element("userSettings");
            XElement xmlSettingsLoc;
            if (IsRoaming(value.Property))
                xmlSettingsLoc = xmlSettings.Element("Roaming");
            else xmlSettingsLoc = xmlSettings.Element("PC_" + Environment.MachineName);
            // the serialized value to be saved
            XNode serialized;
            if (value.SerializedValue == null) serialized = new XText("");
            else if (value.Property.SerializeAs == SettingsSerializeAs.Xml)
                serialized = XElement.Parse((string)value.SerializedValue);
            else serialized = new XText((string)value.SerializedValue);
            // check if setting already exists, otherwise create new
            if (xmlSettingsLoc == null)
            {
                if (IsRoaming(value.Property)) xmlSettingsLoc = new XElement("Roaming");
                else xmlSettingsLoc = new XElement("PC_" + Environment.MachineName);
                xmlSettingsLoc.Add(new XElement(scope,
                    new XElement(value.Name, serialized)));
                xmlSettings.Add(xmlSettingsLoc);
            }
            else
            {
                XElement xmlScope = xmlSettingsLoc.Element(scope);
                if (xmlScope != null)
                {
                    XElement xmlElem = xmlScope.Element(value.Name);
                    if (xmlElem == null) xmlScope.Add(new XElement(value.Name, serialized));
                    else xmlElem.ReplaceAll(serialized);
                }
                else
                {
                    xmlSettingsLoc.Add(new XElement(scope, new XElement(value.Name, serialized)));
                }
            }
        }

        // Iterates through the properties' attributes to determine whether it's user-scoped or application-scoped.
        private bool IsUserScoped(SettingsProperty prop)
        {
            foreach (DictionaryEntry d in prop.Attributes)
            {
                Attribute a = (Attribute)d.Value;
                if (a.GetType() == typeof(UserScopedSettingAttribute))
                    return true;
            }
            return false;
        }

        // Iterates through the properties' attributes to determine whether it's set to roam.
        private bool IsRoaming(SettingsProperty prop)
        {
            foreach (DictionaryEntry d in prop.Attributes)
            {
                Attribute a = (Attribute)d.Value;
                if (a.GetType() == typeof(SettingsManageabilityAttribute))
                    return true;
            }
            return false;
        }
    }
}
