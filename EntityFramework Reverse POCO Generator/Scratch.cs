﻿// This file is solely use as a helper for editing the T4 template EntityCore.ttinclude
// Edits are made here, and copy+pasted into the T4 template

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace EntityFramework_Reverse_POCO_Generator
{
    internal class GeneratedTextTransformation
    {
        #region do not include

        public object Host;

        private void Warning(string format)
        {
            Console.WriteLine(format);
        }

        private void WriteLine(string s, string p = "", string e = "")
        {
            Console.WriteLine(s + p + e);
        }

        public class EnvDTE
        {
            #region Nested type: DTE

            public class DTE
            {
                public Array ActiveSolutionProjects;
            }

            #endregion

            #region Nested type: Project

            public class Project
            {
                public string FileName;
                public string FullName;
                public IEnumerable<ProjectItem> ProjectItems;
            }

            #endregion

            #region Nested type: ProjectItem

            public class ProjectItem
            {
                public string Name;
            }

            #endregion
        };

        #endregion

        string SchemaName = null;
        string ConnectionStringName = "";
        bool IncludeViews = false;
        string Namespace = "";

        // Use the following table/view name regex filters to include or exclude tables/views
        // Exclude filters are checked first and tables matching filters are removed.
        //  * If left null, none are excluded.
        //  * If not null, any tables matching the regex are excluded.
        // Include filters are checked second.
        //  * If left null, all are included.
        //  * If not null, only the tables matching the regex are included.
        //  Example:    TableFilterExclude = new Regex(".*auto.*");
        //              TableFilterInclude = new Regex("(.*_fr_.*)|(data_.*)");
        Regex TableFilterExclude = null;
        Regex TableFilterInclude = null;

        private string _connectionString = "";
        private string _providerName = "";

        private static readonly Regex RxCleanUp = new Regex(@"[^\w\d_]", RegexOptions.Compiled);

        private static readonly Func<string, string> CleanUp = (str) =>
        {
            str = RxCleanUp.Replace(str, "_");
            if(char.IsDigit(str[0]))
                str = "_" + str;

            return str;
        };

        public string ConnectionString
        {
            get
            {
                InitConnectionString();
                return _connectionString;
            }
        }

        public string ProviderName
        {
            get
            {
                InitConnectionString();
                return _providerName;
            }
        }

        private static string CheckNullable(Column col)
        {
            string result = "";
            if(col.IsNullable &&
                col.PropertyType != "byte[]" &&
                col.PropertyType != "string" &&
                col.PropertyType != "Microsoft.SqlServer.Types.SqlGeography" &&
                col.PropertyType != "Microsoft.SqlServer.Types.SqlGeometry")
                result = "?";
            return result;
        }

        private string GetConnectionString(ref string connectionStringName, out string providerName)
        {
            var currentProject = GetCurrentProject();

            providerName = null;
            string result = "";
            var configFile = new ExeConfigurationFileMap { ExeConfigFilename = GetConfigPath() };

            if(string.IsNullOrEmpty(configFile.ExeConfigFilename))
                throw new ApplicationException("The project does not contain App.config or Web.config file.");

            var config = ConfigurationManager.OpenMappedExeConfiguration(configFile, ConfigurationUserLevel.None);
            var connSection = config.ConnectionStrings;

            if(!string.IsNullOrEmpty(connectionStringName))
            {
                // Get the named connection string
                try
                {
                    result = connSection.ConnectionStrings[connectionStringName].ConnectionString;
                    providerName = connSection.ConnectionStrings[connectionStringName].ProviderName;
                }
                catch
                {
                    result = "There is no connection string name called '" + connectionStringName + "'";
                }
            }

            // No named connection string, therefore retrieve the last one from config
            if(connSection.ConnectionStrings.Count > 0)
            {
                var connectionString = connSection.ConnectionStrings[connSection.ConnectionStrings.Count - 1];
                connectionStringName = connectionString.Name;
                result = connectionString.ConnectionString;
                providerName = connectionString.ProviderName;
            }

            return result;
        }

        private void InitConnectionString()
        {
            if(!String.IsNullOrEmpty(_connectionString))
                return;

            _connectionString = GetConnectionString(ref ConnectionStringName, out _providerName);

            if(!_connectionString.Contains("|DataDirectory|"))
                return;

            // Replace data directory path
            string dataFilePath = GetDataDirectory();
            _connectionString = _connectionString.Replace("|DataDirectory|", dataFilePath);
        }


        public EnvDTE.Project GetCurrentProject()
        {
            IServiceProvider _ServiceProvider = (IServiceProvider)Host;
            if(_ServiceProvider == null)
                throw new Exception("Host property returned unexpected value (null)");

            EnvDTE.DTE dte = (EnvDTE.DTE)_ServiceProvider.GetService(typeof(EnvDTE.DTE));
            if(dte == null)
                throw new Exception("Unable to retrieve EnvDTE.DTE");

            Array activeSolutionProjects = (Array)dte.ActiveSolutionProjects;
            if(activeSolutionProjects == null)
                throw new Exception("DTE.ActiveSolutionProjects returned null");

            EnvDTE.Project dteProject = (EnvDTE.Project)activeSolutionProjects.GetValue(0);
            if(dteProject == null)
                throw new Exception("DTE.ActiveSolutionProjects[0] returned null");

            return dteProject;
        }

        private string GetProjectPath()
        {
            EnvDTE.Project project = GetCurrentProject();
            var info = new FileInfo(project.FullName);
            return info.Directory == null ? string.Empty : info.Directory.FullName;
        }

        private string GetConfigPath()
        {
            EnvDTE.Project project = GetCurrentProject();
            foreach(EnvDTE.ProjectItem item in project.ProjectItems)
            {
                // if it is the app.config file, then open it up
                if(item.Name.Equals("App.config", StringComparison.InvariantCultureIgnoreCase) || item.Name.Equals("Web.config", StringComparison.InvariantCultureIgnoreCase))
                    return GetProjectPath() + "\\" + item.Name;
            }
            return String.Empty;
        }

        public string GetDataDirectory()
        {
            var project = GetCurrentProject();
            return Path.GetDirectoryName(project.FileName) + "\\App_Data\\";
        }

        private static string ZapPassword(string connectionString)
        {
            var rx = new Regex("password=.*;", RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.IgnoreCase);
            return rx.Replace(connectionString, "password=**zapped**;");
        }


        private Tables LoadTables()
        {
            InitConnectionString();

            WriteLine("// This file was automatically generated.");
            WriteLine("// Do not make changes directly to this file - edit the template instead.");
            WriteLine("// ");
            WriteLine("// The following connection settings were used to generate this file");
            WriteLine("// ");
            WriteLine("//     Connection String Name: `{0}`", ConnectionStringName);
            WriteLine("//     Connection String:      `{0}`", ZapPassword(ConnectionString));
            WriteLine("");

            DbProviderFactory factory;
            try
            {
                factory = DbProviderFactories.GetFactory(ProviderName);
            }
            catch(Exception x)
            {
                string error = x.Message.Replace("\r\n", "\n").Replace("\n", " ");
                Warning(string.Format("Failed to load provider `{0}` - {1}", ProviderName, error));
                WriteLine("");
                WriteLine("// -----------------------------------------------------------------------------------------");
                WriteLine("// Failed to load provider `{0}` - {1}", ProviderName, error);
                WriteLine("// -----------------------------------------------------------------------------------------");
                WriteLine("");
                return new Tables();
            }

            try
            {
                using(DbConnection conn = factory.CreateConnection())
                {
                    conn.ConnectionString = ConnectionString;
                    conn.Open();

                    var reader = new SqlServerSchemaReader(conn, factory) { Outer = this };
                    var result = reader.ReadSchema();

                    // Remove unrequired tables/views
                    for(int i = result.Count - 1; i >= 0; i--)
                    {
                        if(SchemaName != null && String.Compare(result[i].Schema, SchemaName, StringComparison.OrdinalIgnoreCase) != 0)
                        {
                            result.RemoveAt(i);
                            continue;
                        }
                        if(!IncludeViews && result[i].IsView)
                        {
                            result.RemoveAt(i);
                            continue;
                        }
                        if(TableFilterExclude != null && TableFilterExclude.IsMatch(result[i].Name))
                        {
                            result.RemoveAt(i);
                            continue;
                        }
                        if(TableFilterInclude != null && !TableFilterInclude.IsMatch(result[i].Name))
                        {
                            result.RemoveAt(i);
                        }
                    }

                    result = reader.ReadForeignKeys(result);

                    conn.Close();
                    return result;
                }
            }
            catch(Exception x)
            {
                string error = x.Message.Replace("\r\n", "\n").Replace("\n", " ");
                Warning(string.Format("Failed to read database schema - {0}", error));
                WriteLine("");
                WriteLine("// -----------------------------------------------------------------------------------------");
                WriteLine("// Failed to read database schema - {0}", error);
                WriteLine("// -----------------------------------------------------------------------------------------");
                WriteLine("");
                return new Tables();
            }
        }

        #region Nested type: Column

        public class Column
        {
            public string Name;
            public int DateTimePrecision;
            public string Default;
            public int MaxLength;
            public int Precision;
            public string PropertyName;
            public string PropertyNameHumanCase;
            public string PropertyType;
            public int Scale;

            public bool IsIdentity;
            public bool IsNullable;
            public bool IsPrimaryKey;
            public bool IsStoreGenerated;

            public string Config;
            public string ConfigFk;
            public string Entity;
            public string EntityFk;

            private void SetupEntity()
            {
                Entity = string.Format("public virtual {0}{1} {2} {3}", PropertyType, CheckNullable(this), PropertyNameHumanCase, "{ get; set; }");
            }

            private void SetupConfig()
            {
                Config = string.Format("Property(x => x.{0}).HasColumnName(\"{1}\"){2}{3}{4}{5};", PropertyNameHumanCase, PropertyName,
                                            (IsNullable) ? ".IsOptional()" : ".IsRequired()",
                                            (MaxLength > 0) ? ".HasMaxLength(" + MaxLength + ")" : string.Empty,
                                            (IsIdentity) ? ".HasDatabaseGeneratedOption(DatabaseGeneratedOption.Identity)" : string.Empty,
                                            (IsStoreGenerated) ? ".HasDatabaseGeneratedOption(DatabaseGeneratedOption.Computed)" : string.Empty);
            }

            public void SetupEntityAndConfig()
            {
                SetupEntity();
                SetupConfig();
            }
        }

        #endregion

        #region Nested type: Inflector

        /// <summary>
        /// Summary for the Inflector class
        /// </summary>
        public static class Inflector
        {
            private static readonly List<InflectorRule> Plurals = new List<InflectorRule>();
            private static readonly List<InflectorRule> Singulars = new List<InflectorRule>();
            private static readonly List<string> Uncountables = new List<string>();

            /// <summary>
            /// Initializes the <see cref="Inflector"/> class.
            /// </summary>
            static Inflector()
            {
                AddPluralRule("$", "s");
                AddPluralRule("s$", "s");
                AddPluralRule("(ax|test)is$", "$1es");
                AddPluralRule("(octop|vir)us$", "$1i");
                AddPluralRule("(alias|status)$", "$1es");
                AddPluralRule("(bu)s$", "$1ses");
                AddPluralRule("(buffal|tomat)o$", "$1oes");
                AddPluralRule("([ti])um$", "$1a");
                AddPluralRule("sis$", "ses");
                AddPluralRule("(?:([^f])fe|([lr])f)$", "$1$2ves");
                AddPluralRule("(hive)$", "$1s");
                AddPluralRule("([^aeiouy]|qu)y$", "$1ies");
                AddPluralRule("(x|ch|ss|sh)$", "$1es");
                AddPluralRule("(matr|vert|ind)ix|ex$", "$1ices");
                AddPluralRule("([m|l])ouse$", "$1ice");
                AddPluralRule("^(ox)$", "$1en");
                AddPluralRule("(quiz)$", "$1zes");

                AddSingularRule("s$", String.Empty);
                AddSingularRule("ss$", "ss");
                AddSingularRule("(n)ews$", "$1ews");
                AddSingularRule("([ti])a$", "$1um");
                AddSingularRule("((a)naly|(b)a|(d)iagno|(p)arenthe|(p)rogno|(s)ynop|(t)he)ses$", "$1$2sis");
                AddSingularRule("(^analy)ses$", "$1sis");
                AddSingularRule("([^f])ves$", "$1fe");
                AddSingularRule("(hive)s$", "$1");
                AddSingularRule("(tive)s$", "$1");
                AddSingularRule("([lr])ves$", "$1f");
                AddSingularRule("([^aeiouy]|qu)ies$", "$1y");
                AddSingularRule("(s)eries$", "$1eries");
                AddSingularRule("(m)ovies$", "$1ovie");
                AddSingularRule("(x|ch|ss|sh)es$", "$1");
                AddSingularRule("([m|l])ice$", "$1ouse");
                AddSingularRule("(bus)es$", "$1");
                AddSingularRule("(o)es$", "$1");
                AddSingularRule("(shoe)s$", "$1");
                AddSingularRule("(cris|ax|test)es$", "$1is");
                AddSingularRule("(octop|vir)i$", "$1us");
                AddSingularRule("(alias|status)$", "$1");
                AddSingularRule("(alias|status)es$", "$1");
                AddSingularRule("^(ox)en", "$1");
                AddSingularRule("(vert|ind)ices$", "$1ex");
                AddSingularRule("(matr)ices$", "$1ix");
                AddSingularRule("(quiz)zes$", "$1");

                AddIrregularRule("person", "people");
                AddIrregularRule("man", "men");
                AddIrregularRule("child", "children");
                AddIrregularRule("sex", "sexes");
                AddIrregularRule("tax", "taxes");
                AddIrregularRule("move", "moves");

                AddUnknownCountRule("equipment");
                AddUnknownCountRule("information");
                AddUnknownCountRule("rice");
                AddUnknownCountRule("money");
                AddUnknownCountRule("species");
                AddUnknownCountRule("series");
                AddUnknownCountRule("fish");
                AddUnknownCountRule("sheep");
            }

            /// <summary>
            /// Adds the irregular rule.
            /// </summary>
            /// <param name="singular">The singular.</param>
            /// <param name="plural">The plural.</param>
            private static void AddIrregularRule(string singular, string plural)
            {
                AddPluralRule(String.Concat("(", singular[0], ")", singular.Substring(1), "$"), String.Concat("$1", plural.Substring(1)));
                AddSingularRule(String.Concat("(", plural[0], ")", plural.Substring(1), "$"), String.Concat("$1", singular.Substring(1)));
            }

            /// <summary>
            /// Adds the unknown count rule.
            /// </summary>
            /// <param name="word">The word.</param>
            private static void AddUnknownCountRule(string word)
            {
                Uncountables.Add(word.ToLower());
            }

            /// <summary>
            /// Adds the plural rule.
            /// </summary>
            /// <param name="rule">The rule.</param>
            /// <param name="replacement">The replacement.</param>
            private static void AddPluralRule(string rule, string replacement)
            {
                Plurals.Add(new InflectorRule(rule, replacement));
            }

            /// <summary>
            /// Adds the singular rule.
            /// </summary>
            /// <param name="rule">The rule.</param>
            /// <param name="replacement">The replacement.</param>
            private static void AddSingularRule(string rule, string replacement)
            {
                Singulars.Add(new InflectorRule(rule, replacement));
            }

            /// <summary>
            /// Makes the plural.
            /// </summary>
            /// <param name="word">The word.</param>
            /// <returns></returns>
            public static string MakePlural(string word)
            {
                return ApplyRules(Plurals, word);
            }

            /// <summary>
            /// Makes the singular.
            /// </summary>
            /// <param name="word">The word.</param>
            /// <returns></returns>
            public static string MakeSingular(string word)
            {
                return ApplyRules(Singulars, word);
            }

            /// <summary>
            /// Applies the rules.
            /// </summary>
            /// <param name="rules">The rules.</param>
            /// <param name="word">The word.</param>
            /// <returns></returns>
            private static string ApplyRules(IList<InflectorRule> rules, string word)
            {
                string result = word;
                if(!Uncountables.Contains(word.ToLower()))
                {
                    for(int i = rules.Count - 1; i >= 0; i--)
                    {
                        string currentPass = rules[i].Apply(word);
                        if(currentPass != null)
                        {
                            result = currentPass;
                            break;
                        }
                    }
                }
                return result;
            }

            /// <summary>
            /// Converts the string to title case.
            /// </summary>
            /// <param name="word">The word.</param>
            /// <returns></returns>
            public static string ToTitleCase(string word)
            {
                string s = Regex.Replace(ToHumanCase(AddUnderscores(word)), @"\b([a-z])", match => match.Captures[0].Value.ToUpper());
                bool digit = false;
                string a = string.Empty;
                foreach(char c in s)
                {
                    if(Char.IsDigit(c))
                    {
                        digit = true;
                        a = a + c;
                    }
                    else
                    {
                        if(digit && Char.IsLower(c))
                            a = a + Char.ToUpper(c);
                        else
                            a = a + c;
                        digit = false;
                    }
                }
                return a;
            }

            /// <summary>
            /// Converts the string to human case.
            /// </summary>
            /// <param name="lowercaseAndUnderscoredWord">The lowercase and underscored word.</param>
            /// <returns></returns>
            public static string ToHumanCase(string lowercaseAndUnderscoredWord)
            {
                return MakeInitialCaps(Regex.Replace(lowercaseAndUnderscoredWord, @"_", " "));
            }


            /// <summary>
            /// Adds the underscores.
            /// </summary>
            /// <param name="pascalCasedWord">The pascal cased word.</param>
            /// <returns></returns>
            public static string AddUnderscores(string pascalCasedWord)
            {
                return
                    Regex.Replace(Regex.Replace(Regex.Replace(pascalCasedWord, @"([A-Z]+)([A-Z][a-z])", "$1_$2"), @"([a-z\d])([A-Z])", "$1_$2"), @"[-\s]", "_").ToLower();
            }

            /// <summary>
            /// Makes the initial caps.
            /// </summary>
            /// <param name="word">The word.</param>
            /// <returns></returns>
            public static string MakeInitialCaps(string word)
            {
                return String.Concat(word.Substring(0, 1).ToUpper(), word.Substring(1).ToLower());
            }

            /// <summary>
            /// Makes the initial lower case.
            /// </summary>
            /// <param name="word">The word.</param>
            /// <returns></returns>
            public static string MakeInitialLowerCase(string word)
            {
                return String.Concat(word.Substring(0, 1).ToLower(), word.Substring(1));
            }


            /// <summary>
            /// Determine whether the passed string is numeric, by attempting to parse it to a double
            /// </summary>
            /// <param name="str">The string to evaluated for numeric conversion</param>
            /// <returns>
            /// 	<c>true</c> if the string can be converted to a number; otherwise, <c>false</c>.
            /// </returns>
            public static bool IsStringNumeric(string str)
            {
                double result;
                return (double.TryParse(str, NumberStyles.Float, NumberFormatInfo.CurrentInfo, out result));
            }

            /// <summary>
            /// Adds the ordinal suffix.
            /// </summary>
            /// <param name="number">The number.</param>
            /// <returns></returns>
            public static string AddOrdinalSuffix(string number)
            {
                if(IsStringNumeric(number))
                {
                    int n = int.Parse(number);
                    int nMod100 = n % 100;

                    if(nMod100 >= 11 && nMod100 <= 13)
                        return String.Concat(number, "th");

                    switch(n % 10)
                    {
                        case 1:
                            return String.Concat(number, "st");
                        case 2:
                            return String.Concat(number, "nd");
                        case 3:
                            return String.Concat(number, "rd");
                        default:
                            return String.Concat(number, "th");
                    }
                }
                return number;
            }

            /// <summary>
            /// Converts the underscores to dashes.
            /// </summary>
            /// <param name="underscoredWord">The underscored word.</param>
            /// <returns></returns>
            public static string ConvertUnderscoresToDashes(string underscoredWord)
            {
                return underscoredWord.Replace('_', '-');
            }

            #region Nested type: InflectorRule

            /// <summary>
            /// Summary for the InflectorRule class
            /// </summary>
            private class InflectorRule
            {
                private readonly Regex _regex;
                private readonly string _replacement;

                /// <summary>
                /// Initializes a new instance of the <see cref="InflectorRule"/> class.
                /// </summary>
                /// <param name="regexPattern">The regex pattern.</param>
                /// <param name="replacementText">The replacement text.</param>
                public InflectorRule(string regexPattern, string replacementText)
                {
                    _regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
                    _replacement = replacementText;
                }

                /// <summary>
                /// Applies the specified word.
                /// </summary>
                /// <param name="word">The word.</param>
                /// <returns></returns>
                public string Apply(string word)
                {
                    if(!_regex.IsMatch(word))
                        return null;

                    string replace = _regex.Replace(word, _replacement);
                    if(word == word.ToUpper())
                        replace = replace.ToUpper();

                    return replace;
                }
            }

            #endregion
        }

        #endregion

        #region Nested type: SchemaReader

        private abstract class SchemaReader
        {
            protected readonly DbCommand Cmd;

            protected SchemaReader(DbConnection connection, DbProviderFactory factory)
            {
                Cmd = factory.CreateCommand();
                if(Cmd != null)
                    Cmd.Connection = connection;
            }

            public GeneratedTextTransformation Outer;
            public abstract Tables ReadSchema();
            public abstract Tables ReadForeignKeys(Tables result);

            protected void WriteLine(string o)
            {
                Outer.WriteLine(o);
            }
        }

        #endregion

        #region Nested type: SqlServerSchemaReader

        private class SqlServerSchemaReader : SchemaReader
        {
            private const string TableSQL = @"
SELECT  [Extent1].[SchemaName],
        [Extent1].[Name] AS TableName,
        [Extent1].[TABLE_TYPE] AS TableType,
        [UnionAll1].[Ordinal],
        [UnionAll1].[Name] AS ColumnName,
        [UnionAll1].[IsNullable],
        [UnionAll1].[TypeName],
        ISNULL([UnionAll1].[MaxLength],0) AS MaxLength,
        ISNULL([UnionAll1].[Precision], 0) AS Precision,
		ISNULL([UnionAll1].[Default], '') AS [Default],
        ISNULL([UnionAll1].[DateTimePrecision], '') AS [DateTimePrecision],
        ISNULL([UnionAll1].[Scale], 0) AS Scale,
        [UnionAll1].[IsIdentity],
        [UnionAll1].[IsStoreGenerated],
        CASE WHEN ([Project5].[C2] IS NULL) THEN CAST(0 AS BIT)
             ELSE [Project5].[C2]
        END AS PrimaryKey
FROM    (
         SELECT QUOTENAME(TABLE_SCHEMA) + QUOTENAME(TABLE_NAME) [Id],
                TABLE_SCHEMA [SchemaName],
                TABLE_NAME [Name],
                TABLE_TYPE
         FROM   INFORMATION_SCHEMA.TABLES
         WHERE  TABLE_TYPE IN ('BASE TABLE', 'VIEW')
        ) AS [Extent1]
        INNER JOIN (
                    SELECT  [Extent2].[Id] AS [Id],
                            [Extent2].[Name] AS [Name],
                            [Extent2].[Ordinal] AS [Ordinal],
                            [Extent2].[IsNullable] AS [IsNullable],
                            [Extent2].[TypeName] AS [TypeName],
                            [Extent2].[MaxLength] AS [MaxLength],
                            [Extent2].[Precision] AS [Precision],
                            [Extent2].[Default],
                            [Extent2].[DateTimePrecision] AS [DateTimePrecision],
                            [Extent2].[Scale] AS [Scale],
                            [Extent2].[IsIdentity] AS [IsIdentity],
                            [Extent2].[IsStoreGenerated] AS [IsStoreGenerated],
                            0 AS [C1],
                            [Extent2].[ParentId] AS [ParentId]
                    FROM    (
                             SELECT QUOTENAME(c.TABLE_SCHEMA) + QUOTENAME(c.TABLE_NAME) + QUOTENAME(c.COLUMN_NAME) [Id],
                                    QUOTENAME(c.TABLE_SCHEMA) + QUOTENAME(c.TABLE_NAME) [ParentId],
                                    c.COLUMN_NAME [Name],
                                    c.ORDINAL_POSITION [Ordinal],
                                    CAST(CASE c.IS_NULLABLE
                                           WHEN 'YES' THEN 1
                                           WHEN 'NO' THEN 0
                                           ELSE 0
                                         END AS BIT) [IsNullable],
                                    CASE WHEN c.DATA_TYPE IN ('varchar', 'nvarchar', 'varbinary')
                                              AND c.CHARACTER_MAXIMUM_LENGTH = -1 THEN c.DATA_TYPE + '(max)'
                                         ELSE c.DATA_TYPE
                                    END AS [TypeName],
                                    c.CHARACTER_MAXIMUM_LENGTH [MaxLength],
                                    CAST(c.NUMERIC_PRECISION AS INTEGER) [Precision],
                                    CAST(c.DATETIME_PRECISION AS INTEGER) [DateTimePrecision],
                                    CAST(c.NUMERIC_SCALE AS INTEGER) [Scale],
                                    c.COLLATION_CATALOG [CollationCatalog],
                                    c.COLLATION_SCHEMA [CollationSchema],
                                    c.COLLATION_NAME [CollationName],
                                    c.CHARACTER_SET_CATALOG [CharacterSetCatalog],
                                    c.CHARACTER_SET_SCHEMA [CharacterSetSchema],
                                    c.CHARACTER_SET_NAME [CharacterSetName],
                                    CAST(0 AS BIT) AS [IsMultiSet],
                                    CAST(COLUMNPROPERTY(OBJECT_ID(QUOTENAME(c.TABLE_SCHEMA) + '.' + QUOTENAME(c.TABLE_NAME)), c.COLUMN_NAME, 'IsIdentity') AS BIT) AS [IsIdentity],
                                    CAST(COLUMNPROPERTY(OBJECT_ID(QUOTENAME(c.TABLE_SCHEMA) + '.' + QUOTENAME(c.TABLE_NAME)), c.COLUMN_NAME, 'IsComputed')
                                    | CASE WHEN c.DATA_TYPE = 'timestamp' THEN 1
                                           ELSE 0
                                      END AS BIT) AS [IsStoreGenerated],
                                    c.COLUMN_DEFAULT AS [Default]
                             FROM   INFORMATION_SCHEMA.COLUMNS c
                                    INNER JOIN INFORMATION_SCHEMA.TABLES t
                                        ON c.TABLE_CATALOG = t.TABLE_CATALOG
                                           AND c.TABLE_SCHEMA = t.TABLE_SCHEMA
                                           AND c.TABLE_NAME = t.TABLE_NAME
                                           AND t.TABLE_TYPE IN ('BASE TABLE', 'VIEW')
                            ) AS [Extent2]
                    UNION ALL
                    SELECT  [Extent3].[Id] AS [Id],
                            [Extent3].[Name] AS [Name],
                            [Extent3].[Ordinal] AS [Ordinal],
                            [Extent3].[IsNullable] AS [IsNullable],
                            [Extent3].[TypeName] AS [TypeName],
                            [Extent3].[MaxLength] AS [MaxLength],
                            [Extent3].[Precision] AS [Precision],
                            [Extent3].[Default],
                            [Extent3].[DateTimePrecision] AS [DateTimePrecision],
                            [Extent3].[Scale] AS [Scale],
                            [Extent3].[IsIdentity] AS [IsIdentity],
                            [Extent3].[IsStoreGenerated] AS [IsStoreGenerated],
                            6 AS [C1],
                            [Extent3].[ParentId] AS [ParentId]
                    FROM    (
                             SELECT QUOTENAME(c.TABLE_SCHEMA) + QUOTENAME(c.TABLE_NAME) + QUOTENAME(c.COLUMN_NAME) [Id],
                                    QUOTENAME(c.TABLE_SCHEMA) + QUOTENAME(c.TABLE_NAME) [ParentId],
                                    c.COLUMN_NAME [Name],
                                    c.ORDINAL_POSITION [Ordinal],
                                    CAST(CASE c.IS_NULLABLE
                                           WHEN 'YES' THEN 1
                                           WHEN 'NO' THEN 0
                                           ELSE 0
                                         END AS BIT) [IsNullable],
                                    CASE WHEN c.DATA_TYPE IN ('varchar', 'nvarchar', 'varbinary')
                                              AND c.CHARACTER_MAXIMUM_LENGTH = -1 THEN c.DATA_TYPE + '(max)'
                                         ELSE c.DATA_TYPE
                                    END AS [TypeName],
                                    c.CHARACTER_MAXIMUM_LENGTH [MaxLength],
                                    CAST(c.NUMERIC_PRECISION AS INTEGER) [Precision],
                                    CAST(c.DATETIME_PRECISION AS INTEGER) AS [DateTimePrecision],
                                    CAST(c.NUMERIC_SCALE AS INTEGER) [Scale],
                                    c.COLLATION_CATALOG [CollationCatalog],
                                    c.COLLATION_SCHEMA [CollationSchema],
                                    c.COLLATION_NAME [CollationName],
                                    c.CHARACTER_SET_CATALOG [CharacterSetCatalog],
                                    c.CHARACTER_SET_SCHEMA [CharacterSetSchema],
                                    c.CHARACTER_SET_NAME [CharacterSetName],
                                    CAST(0 AS BIT) AS [IsMultiSet],
                                    CAST(COLUMNPROPERTY(OBJECT_ID(QUOTENAME(c.TABLE_SCHEMA) + '.' + QUOTENAME(c.TABLE_NAME)), c.COLUMN_NAME, 'IsIdentity') AS BIT) AS [IsIdentity],
                                    CAST(COLUMNPROPERTY(OBJECT_ID(QUOTENAME(c.TABLE_SCHEMA) + '.' + QUOTENAME(c.TABLE_NAME)), c.COLUMN_NAME, 'IsComputed')
                                    | CASE WHEN c.DATA_TYPE = 'timestamp' THEN 1
                                           ELSE 0
                                      END AS BIT) AS [IsStoreGenerated],
                                    c.COLUMN_DEFAULT [Default]
                             FROM   INFORMATION_SCHEMA.COLUMNS c
                                    INNER JOIN INFORMATION_SCHEMA.VIEWS v
                                        ON c.TABLE_CATALOG = v.TABLE_CATALOG
                                           AND c.TABLE_SCHEMA = v.TABLE_SCHEMA
                                           AND c.TABLE_NAME = v.TABLE_NAME
                             WHERE  NOT (
                                         v.TABLE_SCHEMA = 'dbo'
                                         AND v.TABLE_NAME IN ('syssegments', 'sysconstraints')
                                         AND SUBSTRING(CAST(SERVERPROPERTY('productversion') AS VARCHAR(20)), 1, 1) = 8
                                        )
                            ) AS [Extent3]
                   ) AS [UnionAll1]
            ON (0 = [UnionAll1].[C1])
               AND ([Extent1].[Id] = [UnionAll1].[ParentId])
        LEFT OUTER JOIN (
                         SELECT [UnionAll2].[Id] AS [C1],
                                CAST(1 AS BIT) AS [C2]
                         FROM   (
                                 SELECT QUOTENAME(tc.CONSTRAINT_SCHEMA) + QUOTENAME(tc.CONSTRAINT_NAME) [Id],
                                        QUOTENAME(tc.TABLE_SCHEMA) + QUOTENAME(tc.TABLE_NAME) [ParentId],
                                        tc.CONSTRAINT_NAME [Name],
                                        tc.CONSTRAINT_TYPE [ConstraintType],
                                        CAST(CASE tc.IS_DEFERRABLE
                                               WHEN 'NO' THEN 0
                                               ELSE 1
                                             END AS BIT) [IsDeferrable],
                                        CAST(CASE tc.INITIALLY_DEFERRED
                                               WHEN 'NO' THEN 0
                                               ELSE 1
                                             END AS BIT) [IsInitiallyDeferred]
                                 FROM   INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                                 WHERE  tc.TABLE_NAME IS NOT NULL
                                ) AS [Extent4]
                                INNER JOIN (
                                            SELECT  7 AS [C1],
                                                    [Extent5].[ConstraintId] AS [ConstraintId],
                                                    [Extent6].[Id] AS [Id]
                                            FROM    (
                                                     SELECT QUOTENAME(CONSTRAINT_SCHEMA) + QUOTENAME(CONSTRAINT_NAME) [ConstraintId],
                                                            QUOTENAME(TABLE_SCHEMA) + QUOTENAME(TABLE_NAME) + QUOTENAME(COLUMN_NAME) [ColumnId]
                                                     FROM   INFORMATION_SCHEMA.KEY_COLUMN_USAGE
                                                    ) AS [Extent5]
                                                    INNER JOIN (
                                                                SELECT  QUOTENAME(c.TABLE_SCHEMA) + QUOTENAME(c.TABLE_NAME) + QUOTENAME(c.COLUMN_NAME) [Id],
                                                                        QUOTENAME(c.TABLE_SCHEMA) + QUOTENAME(c.TABLE_NAME) [ParentId],
                                                                        c.COLUMN_NAME [Name],
                                                                        c.ORDINAL_POSITION [Ordinal],
                                                                        CAST(CASE c.IS_NULLABLE
                                                                               WHEN 'YES' THEN 1
                                                                               WHEN 'NO' THEN 0
                                                                               ELSE 0
                                                                             END AS BIT) [IsNullable],
                                                                        CASE WHEN c.DATA_TYPE IN ('varchar', 'nvarchar', 'varbinary')
                                                                                  AND c.CHARACTER_MAXIMUM_LENGTH = -1 THEN c.DATA_TYPE + '(max)'
                                                                             ELSE c.DATA_TYPE
                                                                        END AS [TypeName],
                                                                        c.CHARACTER_MAXIMUM_LENGTH [MaxLength],
                                                                        CAST(c.NUMERIC_PRECISION AS INTEGER) [Precision],
                                                                        CAST(c.DATETIME_PRECISION AS INTEGER) [DateTimePrecision],
                                                                        CAST(c.NUMERIC_SCALE AS INTEGER) [Scale],
                                                                        c.COLLATION_CATALOG [CollationCatalog],
                                                                        c.COLLATION_SCHEMA [CollationSchema],
                                                                        c.COLLATION_NAME [CollationName],
                                                                        c.CHARACTER_SET_CATALOG [CharacterSetCatalog],
                                                                        c.CHARACTER_SET_SCHEMA [CharacterSetSchema],
                                                                        c.CHARACTER_SET_NAME [CharacterSetName],
                                                                        CAST(0 AS BIT) AS [IsMultiSet],
                                                                        CAST(COLUMNPROPERTY(OBJECT_ID(QUOTENAME(c.TABLE_SCHEMA) + '.' + QUOTENAME(c.TABLE_NAME)),
                                                                                            c.COLUMN_NAME, 'IsIdentity') AS BIT) AS [IsIdentity],
                                                                        CAST(COLUMNPROPERTY(OBJECT_ID(QUOTENAME(c.TABLE_SCHEMA) + '.' + QUOTENAME(c.TABLE_NAME)),
                                                                                            c.COLUMN_NAME, 'IsComputed')
                                                                        | CASE WHEN c.DATA_TYPE = 'timestamp' THEN 1
                                                                               ELSE 0
                                                                          END AS BIT) AS [IsStoreGenerated],
                                                                        c.COLUMN_DEFAULT AS [Default]
                                                                FROM    INFORMATION_SCHEMA.COLUMNS c
                                                                        INNER JOIN INFORMATION_SCHEMA.TABLES t
                                                                            ON c.TABLE_CATALOG = t.TABLE_CATALOG
                                                                               AND c.TABLE_SCHEMA = t.TABLE_SCHEMA
                                                                               AND c.TABLE_NAME = t.TABLE_NAME
                                                                               AND t.TABLE_TYPE IN ('BASE TABLE', 'VIEW')
                                                               ) AS [Extent6]
                                                        ON [Extent6].[Id] = [Extent5].[ColumnId]
                                            UNION ALL
                                            SELECT  11 AS [C1],
                                                    [Extent7].[ConstraintId] AS [ConstraintId],
                                                    [Extent8].[Id] AS [Id]
                                            FROM    (
                                                     SELECT CAST( NULL AS NVARCHAR (1)) [ConstraintId], CAST( NULL AS NVARCHAR (MAX)) [ColumnId] WHERE 1= 2
                                                    ) AS [Extent7]
                                                    INNER JOIN (
                                                                SELECT  QUOTENAME(c.TABLE_SCHEMA) + QUOTENAME(c.TABLE_NAME) + QUOTENAME(c.COLUMN_NAME) [Id],
                                                                        QUOTENAME(c.TABLE_SCHEMA) + QUOTENAME(c.TABLE_NAME) [ParentId],
                                                                        c.COLUMN_NAME [Name],
                                                                        c.ORDINAL_POSITION [Ordinal],
                                                                        CAST(CASE c.IS_NULLABLE
                                                                               WHEN 'YES' THEN 1
                                                                               WHEN 'NO' THEN 0
                                                                               ELSE 0
                                                                             END AS BIT) [IsNullable],
                                                                        CASE WHEN c.DATA_TYPE IN ('varchar', 'nvarchar', 'varbinary')
                                                                                  AND c.CHARACTER_MAXIMUM_LENGTH = -1 THEN c.DATA_TYPE + '(max)'
                                                                             ELSE c.DATA_TYPE
                                                                        END AS [TypeName],
                                                                        c.CHARACTER_MAXIMUM_LENGTH [MaxLength],
                                                                        CAST(c.NUMERIC_PRECISION AS INTEGER) [Precision],
                                                                        CAST(c.DATETIME_PRECISION AS INTEGER) AS [DateTimePrecision],
                                                                        CAST(c.NUMERIC_SCALE AS INTEGER) [Scale],
                                                                        c.COLLATION_CATALOG [CollationCatalog],
                                                                        c.COLLATION_SCHEMA [CollationSchema],
                                                                        c.COLLATION_NAME [CollationName],
                                                                        c.CHARACTER_SET_CATALOG [CharacterSetCatalog],
                                                                        c.CHARACTER_SET_SCHEMA [CharacterSetSchema],
                                                                        c.CHARACTER_SET_NAME [CharacterSetName],
                                                                        CAST(0 AS BIT) AS [IsMultiSet],
                                                                        CAST(COLUMNPROPERTY(OBJECT_ID(QUOTENAME(c.TABLE_SCHEMA) + '.' + QUOTENAME(c.TABLE_NAME)),
                                                                                            c.COLUMN_NAME, 'IsIdentity') AS BIT) AS [IsIdentity],
                                                                        CAST(COLUMNPROPERTY(OBJECT_ID(QUOTENAME(c.TABLE_SCHEMA) + '.' + QUOTENAME(c.TABLE_NAME)),
                                                                                            c.COLUMN_NAME, 'IsComputed')
                                                                        | CASE WHEN c.DATA_TYPE = 'timestamp' THEN 1
                                                                               ELSE 0
                                                                          END AS BIT) AS [IsStoreGenerated],
                                                                        c.COLUMN_DEFAULT [Default]
                                                                FROM    INFORMATION_SCHEMA.COLUMNS c
                                                                        INNER JOIN INFORMATION_SCHEMA.VIEWS v
                                                                            ON c.TABLE_CATALOG = v.TABLE_CATALOG
                                                                               AND c.TABLE_SCHEMA = v.TABLE_SCHEMA
                                                                               AND c.TABLE_NAME = v.TABLE_NAME
                                                                WHERE   NOT (
                                                                             v.TABLE_SCHEMA = 'dbo'
                                                                             AND v.TABLE_NAME IN ('syssegments', 'sysconstraints')
                                                                             AND SUBSTRING(CAST(SERVERPROPERTY('productversion') AS VARCHAR(20)), 1, 1) = 8
                                                                            )
                                                               ) AS [Extent8]
                                                        ON [Extent8].[Id] = [Extent7].[ColumnId]
                                           ) AS [UnionAll2]
                                    ON (7 = [UnionAll2].[C1])
                                       AND ([Extent4].[Id] = [UnionAll2].[ConstraintId])
                         WHERE  [Extent4].[ConstraintType] = N'PRIMARY KEY'
                        ) AS [Project5]
            ON [UnionAll1].[Id] = [Project5].[C1]
WHERE   NOT ([Extent1].[Name] IN ('EdmMetadata', '__MigrationHistory'))";

            private const string ForeignKeySQL = @"
SELECT  FK_Table = FK.TABLE_NAME,
        FK_Column = CU.COLUMN_NAME,
        PK_Table = PrimaryKey.TABLE_NAME,
        PK_Column = PT.COLUMN_NAME,
        Constraint_Name = C.CONSTRAINT_NAME
FROM    INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS C
        INNER JOIN INFORMATION_SCHEMA.TABLE_CONSTRAINTS FK
            ON C.CONSTRAINT_NAME = FK.CONSTRAINT_NAME
        INNER JOIN INFORMATION_SCHEMA.TABLE_CONSTRAINTS PrimaryKey
            ON C.UNIQUE_CONSTRAINT_NAME = PrimaryKey.CONSTRAINT_NAME
        INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE CU
            ON C.CONSTRAINT_NAME = CU.CONSTRAINT_NAME
        INNER JOIN (
                    SELECT  i1.TABLE_NAME,
                            i2.COLUMN_NAME
                    FROM    INFORMATION_SCHEMA.TABLE_CONSTRAINTS i1
                            INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE i2
                                ON i1.CONSTRAINT_NAME = i2.CONSTRAINT_NAME
                    WHERE   i1.CONSTRAINT_TYPE = 'PRIMARY KEY'
                   ) PT
            ON PT.TABLE_NAME = PrimaryKey.TABLE_NAME
ORDER BY FK.TABLE_NAME, CU.COLUMN_NAME";

            public SqlServerSchemaReader(DbConnection connection, DbProviderFactory factory) : base(connection, factory)
            {
            }

            public override Tables ReadSchema()
            {
                var result = new Tables();
                if(Cmd == null)
                    return result;
                
                Cmd.CommandText = TableSQL;

                using(Cmd)
                {
                    using(DbDataReader rdr = Cmd.ExecuteReader())
                    {
                        var rxClean =
                            new Regex(
                                "^(event|Equals|GetHashCode|GetType|ToString|repo|Save|IsNew|Insert|Update|Delete|Exists|SingleOrDefault|Single|First|FirstOrDefault|Fetch|Page|Query)$");
                        var lastTable = string.Empty;
                        Table table = null;
                        while(rdr.Read())
                        {
                            string tableName = rdr["TableName"].ToString();
                            if(lastTable != tableName || table == null)
                            {
                                // The data from the database is not sorted
                                table = result.Find(x => x.Name == tableName);
                                if(table == null)
                                {
                                    table = new Table
                                    {
                                        Name = tableName,
                                        Schema = rdr["SchemaName"].ToString(),
                                        IsView = String.Compare(rdr["TableType"].ToString(), "View", StringComparison.OrdinalIgnoreCase) == 0,

                                        // Will be set later
                                        HasForeignKey = false,
                                        HasNullableColumns = false
                                    };
                                    table.CleanName = CleanUp(table.Name);
                                    table.ClassName = Inflector.MakeSingular(table.CleanName);
                                    table.NameHumanCase = Inflector.ToTitleCase(table.Name).Replace(" ", "").Replace("$", "");

                                    result.Add(table);
                                }
                            }

                            table.Columns.Add(CreateColumn(rdr, rxClean, table));
                        }
                    }
                }

                foreach(Table tbl in result)
                {
                    // Check for property name clashes
                    foreach(Column c in tbl.Columns)
                    {
                        if(tbl.Columns.FindAll(x => x.PropertyNameHumanCase == c.PropertyNameHumanCase).Count > 1)
                            c.PropertyNameHumanCase = c.PropertyName;
                    }

                    tbl.Columns.ForEach(x => x.SetupEntityAndConfig());
                }

                return result;
            }

            public override Tables ReadForeignKeys(Tables result)
            {
                if(Cmd == null)
                    return result;

                Cmd.CommandText = ForeignKeySQL;

                using(Cmd)
                {
                    using(DbDataReader rdr = Cmd.ExecuteReader())
                    {
                        while(rdr.Read())
                        {
                            Table tbl = result.GetTable(rdr["FK_Table"].ToString());
                            if(tbl != null)
                            {
                                string fkColumn = rdr["FK_Column"].ToString();
                                Column col = tbl.Columns.Find(n => n.PropertyName == fkColumn);
                                if(col != null)
                                {
                                    tbl.HasForeignKey = true;
                                    string constraintName = rdr["Constraint_Name"].ToString();

                                    string pkTable = rdr["PK_Table"].ToString();
                                    string pkTableHumanCase = Inflector.ToTitleCase(pkTable).Replace(" ", "").Replace("$", "");
                                    string constraintNameHumanCase = Inflector.ToTitleCase(constraintName).Replace(" ", "").Replace("$", "");
                                    string fkName;
                                    if(col.PropertyNameHumanCase.EndsWith("Id"))
                                        fkName = col.PropertyNameHumanCase.Substring(0, col.PropertyNameHumanCase.Length - 2) + "Fk";
                                    else
                                        fkName = col.PropertyNameHumanCase + "Fk";

                                    col.EntityFk = string.Format("public virtual {0} {1} {2} {3}", pkTableHumanCase, fkName, "{ get; set; } // ",
                                                                 col.PropertyNameHumanCase + " - " + constraintNameHumanCase);

                                    col.ConfigFk = string.Format("Has{0}(a => a.{1}).WithMany().HasForeignKey(b => b.{2}); // {3}",
                                                                 col.IsNullable ? "Optional" : "Required", fkName, col.PropertyNameHumanCase, constraintNameHumanCase);
                                }
                            }
                        }
                    }
                }

                return result;
            }

            private static Column CreateColumn(IDataRecord rdr, Regex rxClean, Table table)
            {
                if(rdr == null)
                    throw new ArgumentNullException("rdr");

                var col = new Column
                {
                    Name = rdr["ColumnName"].ToString(),
                    PropertyType = GetPropertyType(rdr["TypeName"].ToString()),
                    MaxLength = (int)rdr["MaxLength"],
                    Precision = (int)rdr["Precision"],
                    Default = rdr["Default"].ToString(),
                    DateTimePrecision = (int)rdr["DateTimePrecision"],
                    Scale = (int)rdr["Scale"],
                    IsIdentity = rdr["IsIdentity"].ToString() == "1",
                    IsNullable = rdr["IsNullable"].ToString() == "1",
                    IsStoreGenerated = rdr["IsStoreGenerated"].ToString() == "1",
                    IsPrimaryKey = rdr["PrimaryKey"].ToString() == "1"
                };

                col.PropertyName = CleanUp(col.Name);
                col.PropertyName = rxClean.Replace(col.PropertyName, "_$1");

                // Make sure property name doesn't clash with class name
                if(col.PropertyName == table.NameHumanCase)
                    col.PropertyName = "_" + col.PropertyName;

                col.PropertyNameHumanCase = Inflector.ToTitleCase(col.PropertyName).Replace(" ", "");

                // Make sure property name doesn't clash with class name
                if(col.PropertyNameHumanCase == table.NameHumanCase)
                    col.PropertyNameHumanCase = col.PropertyNameHumanCase + "_";

                if(char.IsDigit(col.PropertyNameHumanCase[0]))
                    col.PropertyNameHumanCase = "_" + col.PropertyNameHumanCase;

                if(CheckNullable(col) != string.Empty)
                    table.HasNullableColumns = true;

                return col;
            }

            private static string GetPropertyType(string sqlType)
            {
                string sysType = "string";
                switch(sqlType)
                {
                    case "bigint":
                        sysType = "long";
                        break;
                    case "smallint":
                        sysType = "short";
                        break;
                    case "int":
                        sysType = "int";
                        break;
                    case "uniqueidentifier":
                        sysType = "Guid";
                        break;
                    case "smalldatetime":
                    case "datetime":
                    case "date":
                        sysType = "DateTime";
                        break;
                    case "time":
                        sysType = "TimeSpan";
                        break;
                    case "float":
                        sysType = "double";
                        break;
                    case "real":
                        sysType = "float";
                        break;
                    case "numeric":
                    case "smallmoney":
                    case "decimal":
                    case "money":
                        sysType = "decimal";
                        break;
                    case "tinyint":
                        sysType = "byte";
                        break;
                    case "bit":
                        sysType = "bool";
                        break;
                    case "image":
                    case "binary":
                    case "varbinary":
                    case "timestamp":
                        sysType = "byte[]";
                        break;
                    case "geography":
                        sysType = "Microsoft.SqlServer.Types.SqlGeography";
                        break;
                    case "geometry":
                        sysType = "Microsoft.SqlServer.Types.SqlGeometry";
                        break;
                }
                return sysType;
            }
        }

        #endregion

        #region Nested type: Table

        public class Table
        {
            public string Name;
            public string NameHumanCase;
            public string Schema;
            public string Type;
            public string ClassName;
            public string CleanName;
            public bool IsView;
            public bool HasForeignKey;
            public bool HasNullableColumns;

            public List<Column> Columns;

            public Table()
            {
                Columns = new List<Column>();
            }

            public IEnumerable<Column> PrimaryKeys
            {
                get { return Columns.Where(x => x.IsPrimaryKey); }
            }

            public string PrimaryKeyNameHumanCase()
            {
                var data = PrimaryKeys.Select(x => x.PropertyNameHumanCase);
                return string.Join(", ", data);
            }

            public Column this[string columnName]
            {
                get { return GetColumn(columnName); }
            }

            public Column GetColumn(string columnName)
            {
                return Columns.SingleOrDefault(x => String.Compare(x.Name, columnName, StringComparison.OrdinalIgnoreCase) == 0);
            }
        }

        #endregion

        #region Nested type: Tables

        public class Tables : List<Table>
        {
            public Table this[string tableName]
            {
                get { return GetTable(tableName); }
            }

            public Table GetTable(string tableName)
            {
                return this.SingleOrDefault(x => String.Compare(x.Name, tableName, StringComparison.OrdinalIgnoreCase) == 0);
            }
        }

        #endregion
    }
}