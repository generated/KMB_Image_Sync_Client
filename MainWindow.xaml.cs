using KMB_Image_Sync_Client.PP_SOAP_Service;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace KMB_Image_Sync_Client
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        static string ConfigPath = new FileInfo(System.Reflection.Assembly.GetExecutingAssembly().Location).Directory.FullName + "\\Configuration.txt";
        static string TempFolderPath = new FileInfo(System.Reflection.Assembly.GetExecutingAssembly().Location).Directory.FullName + "\\temp";
        static JSON_Config Configuration = new JSON_Config();
        static string LogFile = "";
        static string ArchiveDirectory = "";

        public static Field[] AssetFields;
        public static ExtendedPublicServiceClient PictureparkService = new ExtendedPublicServiceClient("Default");
        public static CoreInfo coreInfo;

        public MainWindow()
        {
            InitializeComponent();

            //initial check if config exists
            if (!File.Exists(ConfigPath))
            {
                JSON_Config config = new JSON_Config()
                {
                    ArchivPfad = "PFAD_WO_ERSETZTE_BILDER_ABGELEGT_WERDEN",
                    ZielPfad = "PFAD_IN_WELCHE_DIE_HERUNTERGELADENEN_BILDER_ABGELEGT_WERDEN",
                    LogPfad = "PFAD_WO_LOG_DATEIEN_ABGELEGT_WERDEN",
                    PP_CustomerId = 0,
                    PP_ClientGUID = "PP_CLIENT_GUID",
                    PP_Email = "PP_EMAIL",
                    PP_Password = "PP_PASSWORD"
                };

                string strConfig = JsonConvert.SerializeObject(config, Formatting.Indented);

                File.WriteAllText(ConfigPath, strConfig);

                MessageBox.Show("Es scheint es gibt noch keine Konfiguration, bitte konfiguriere zuerst die benötigten Parameter");
                System.Diagnostics.Process.Start(ConfigPath);
            }
            else
            {
                CheckConfiguration();
                fromDate.Text = Configuration.LetzterAbgleich.ToString("dd.MM.yyyy");
            }

            toDate.Text = DateTime.Now.ToString("dd.MM.yyyy");
        }

        private static bool CheckConfiguration()
        {
            //check if config exists
            if (!File.Exists(ConfigPath))
            {
                MessageBox.Show("Konfigurationsdatei kann nicht gefunden werden");
                return false;
            }

            //check if config is deserializable
            try
            {
                Configuration = JsonConvert.DeserializeObject<JSON_Config>(File.ReadAllText(ConfigPath).Replace("\\", "\\\\"));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Die Konfigurationsdatei ist fehlerhaft, bitte korrigiere den Fehler oder lösche die Datei um eine neue anzulegen.");
                return false;
            }

            //check proxy parameters
            try
            {
                var proxy = new WebProxy(ConfigurationManager.AppSettings["address"], true);
                proxy.Credentials = new NetworkCredential(ConfigurationManager.AppSettings["user"], ConfigurationManager.AppSettings["password"]);
                WebRequest.DefaultWebProxy = proxy;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Die Proxy Parameter sind fehlerhaft, bitte prüfe die Angaben in KMB_Image_Sync_Client.exe.config");
                return false;
            }

            if (!Directory.Exists(Configuration.ZielPfad))
            {
                MessageBox.Show("Der in der Konfiguration eingetragene Zielpfad existiert nicht");
                return false;
            }

            if (!Directory.Exists(Configuration.ArchivPfad))
            {
                MessageBox.Show("Der in der Konfiguration eingetragene Archivpfad existiert nicht");
                return false;
            }

            if (!Directory.Exists(Configuration.LogPfad))
            {
                MessageBox.Show("Der in der Konfiguration eingetragene Logpfad existiert nicht");
                return false;
            }

            return true;
        }

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            BackgroundWorker bgw = new BackgroundWorker();
            string from = fromDate.Text;
            string to = toDate.Text;
            bgw.DoWork += (obj, ev) => Bgw_DoWork(from, to);
            bgw.RunWorkerCompleted += Bgw_RunWorkerCompleted;

            loadingScreen.Visibility = Visibility.Visible;

            bgw.RunWorkerAsync();
        }

        private void Bgw_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            loadingScreen.Visibility = Visibility.Hidden;
        }

        private void Bgw_DoWork(string strFrom, string strTo)
        {
            if (CheckConfiguration())
            {
                DateTimeOffset from = new DateTimeOffset();
                DateTimeOffset to = new DateTimeOffset();

                if (!DateTimeOffset.TryParse(strFrom, out from))
                {
                    MessageBox.Show("Ungültiges von Datum");
                    return;
                }
                if (!DateTimeOffset.TryParse(strTo, out to))
                {
                    MessageBox.Show("Ungültiges bis Datum");
                    return;
                }

                if (from > to)
                {
                    MessageBox.Show("Das bis Datum muss nach dem von Datum liegen");
                    return;
                }

                string ArchiveDirectory = Configuration.ArchivPfad + "\\" + DateTime.Now.ToString("yyyy_MM_dd_hh_mm_ss");

                LogFile = DateTime.Now.ToString("yyyy_MM_dd_hh_mm_ss") + "_log.txt";
                Log("Starte abgleich von " + from.ToString("dd.MM.yyyy hh:mm") + " bis " + to.ToString("dd.MM.yyyy hh:mm"));
                Log("Zielverzeichnis: " + Configuration.ZielPfad);
                Log("Archivverzeichnis: " + ArchiveDirectory);
                Log("Logverzeichnis: " + Configuration.LogPfad);

                coreInfo = LoginPP(PictureparkService);

                if (coreInfo == null)
                    return;

                AssetFields = PictureparkService.GetAssetFields(coreInfo);

                //create temp directory for images or wipe content if already exists
                if (!CreateTempDirectory())
                    return;

                //get all aproved assets in given timespan
                if (!GetAprovedAssets(from, to))
                    return;

                //compare and synchronise images
                int failures = CompareAndSyncImages();

                MessageBox.Show("Abgleich abgeschlossen, " + failures + " fehlgeschlagen, mehr Informationen in der Logdatei");
                Log("Abgleich abgeschlossen, " + failures + " Bilder konnten nicht synchronisiert werden");

                //save last sync to config file
                Configuration.LetzterAbgleich = DateTime.Parse(strTo);
                string strConfig = JsonConvert.SerializeObject(Configuration, Formatting.Indented);
                File.WriteAllText(ConfigPath, strConfig);
            }
        }

        private void btnConfig_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(ConfigPath))
                System.Diagnostics.Process.Start(ConfigPath);
        }

        private static bool CreateTempDirectory()
        {
            if (!Directory.Exists(TempFolderPath))
            {
                try
                {
                    Directory.CreateDirectory(TempFolderPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Temporäres Verzeichnis (" + TempFolderPath + ") kann nicht erstellt werden. Vermutlich fehlt die Berechtigung. In der Logdatei findest du mehr Informationen.");
                    Log("[ERROR] Temp-Verzeichnis kann nicht erstellt werden, Grund: " + ex.ToString());
                    return false;
                }
            }
            else
            {
                string file = "";
                try
                {
                    //wipe directory content
                    foreach (string filePath in Directory.GetFiles(TempFolderPath))
                    {
                        file = filePath;
                        File.Delete(filePath);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Die Datei " + file + " kann nicht gelöscht werden, vielleicht ist sie in einem anderen Programm geöffnet? In der Logdatei findest du den mehr Informationen.");
                    Log("[ERROR] Datei in Temp-Verzeichnis kann nicht gelöscht werden, Grund: " + ex.ToString());
                    return false;
                }
            }

            return true;
        }

        private static bool CreateArchiveDirectory()
        {
            try
            {
                ArchiveDirectory = Configuration.ArchivPfad + "\\" + DateTime.Now.ToString("yyyy_MM_dd_hh_mm_ss") + "_Archiv";
                Directory.CreateDirectory(ArchiveDirectory);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Archiv-Verzeichnis (" + TempFolderPath + ") kann nicht erstellt werden. Vermutlich fehlt die Berechtigung. In der Logdatei findest du mehr Informationen.");
                Log("[ERROR] Archiv-Verzeichnis kann nicht erstellt werden, Grund: " + ex.ToString());
                return false;
            }

            return true;
        }

        private static bool GetAprovedAssets(DateTimeOffset from, DateTimeOffset to)
        {
            DateTimeBetweenOperation dateSearchOperator = new DateTimeBetweenOperation() { FieldName = "MPlusLastUpdateDate", AfterDateTime = from, BeforeDateTime = to.AddDays(1) }; //todo: replace fieldname with new picturepark field

            List<ComparisonOperation> comparisonOperations = new List<ComparisonOperation>();
            comparisonOperations.Add(dateSearchOperator);
            AndOperation searchOperations = new AndOperation() { ComparisonOperations = comparisonOperations.ToArray() };

            ExtendedAssetFilter filter = new ExtendedAssetFilter()
            {
                AdditionalSelectFields = new string[] { "AssetName", "OBJID", "FreigabeDatum", "MPlusLastUpdateDate" },
                SearchOperation = searchOperations
            };

            AssetItemCollection collection = PictureparkService.GetAssets(coreInfo, filter);

            //get preview assets
            List<AssetItem> assets = collection.Assets
            .Where(a => a.FieldValues
                .Where(fv => fv.FieldId == GetFieldIdByName(AssetFields.ToList(), "AssetName"))
                .Any(fv => fv.StringValue.Substring(1, 3).ToLower() == "w11")).ToList();

            Log(assets.Count + " Werke wurden in dem Zeitraum geprüft");

            if (assets.Count != 0)
            {
                if (MessageBox.Show(assets.Count + " Werke wurden in diesem Zeitraum geprüft. Diese jetzt herunterladen und abgleichen?", "", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    //create new archive directory 
                    CreateArchiveDirectory();

                    using (var client = new WebClient())
                    {
                        foreach (AssetItem asset in assets)
                        {
                            List<AssetSelection> assetSelection = new List<AssetSelection>() {
                                    new AssetSelection()
                                    {
                                        AssetId = asset.AssetId,
                                        DerivativeDefinitionId = 7 //todo: check which derivative id to pass here
                                    }
                                };

                            //List<ExtendedDerivative> availableDerivatives = PictureparkService.GetDerivatives(coreInfo, assetSelection.ToArray()).ToList();

                            DownloadOptions downloadOptions = new DownloadOptions()
                            {
                                UserAction = UserAction.DerivativeDownload
                            };

                            Download download = PictureparkService.Download(coreInfo, assetSelection.ToArray(), downloadOptions);

                            //download asset into temp directory
                            client.DownloadFile(download.URL, TempFolderPath + "\\" + download.DownloadFileName);

                            Log(download.DownloadFileName + " in temporäres Verzeichnis heruntergeladen");
                        }

                        // continue with local comparison
                        return true;
                    }
                }
                else
                {
                    Log("Abgleich abgebrochen durch Benutzer");
                }
            }
            else
            {
                MessageBox.Show("In diesem Zeitraum wurden keine Werke geprüft");
                Log("In diesem Zeitraum wurden keine Werke geprüft");
            }

            return false;
        }

        private static int CompareAndSyncImages()
        {
            int errors = 0;

            Log("Starte Abgleich der heruntergeladenen Bilder mit Zielverzeichnis");

            //loop downloaded files
            foreach (string sourceFilePath in Directory.GetFiles(TempFolderPath))
            {
                try
                {
                    FileInfo sourceFile = new FileInfo(sourceFilePath);

                    Log("Abgleich für " + sourceFile.Name + ":");

                    string targetFilePath = Configuration.ZielPfad + "\\" + sourceFile.Name;

                    //check if file exists in target directory
                    if (File.Exists(targetFilePath))
                    {
                        Log("Bild existiert bereits in Zielverzeichnis, vergleiche Hash..");

                        using (var md5 = MD5.Create())
                        {
                            string sourceHash;
                            string targetHash;

                            using (var stream = File.OpenRead(sourceFile.FullName))
                            {
                                sourceHash = BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", "").ToLower();
                            }

                            using (var stream = File.OpenRead(targetFilePath))
                            {
                                targetHash = BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", "").ToLower();
                            }

                            if (sourceHash == targetHash)
                            {
                                Log("Hashes sind identisch, Bild wird nicht kopiert");
                            }
                            else
                            {
                                Log("Hashes sind unterschiedlich, Bild wird kopiert und bestehendes in Archiv verschoben");

                                //move old image to archive
                                File.Move(targetFilePath, ArchiveDirectory + "\\" + sourceFile.Name);

                                //move new image to target directory
                                File.Copy(sourceFile.FullName, targetFilePath);
                            }
                        }
                    }
                    else
                    {
                        Log("Bild existiert noch nicht in Zielverzeichnis und wird kopiert");
                        File.Copy(sourceFile.FullName, targetFilePath);
                    }
                }
                catch (Exception ex)
                {
                    Log("[ERROR] Fehler bei abgleich von Bild " + sourceFilePath + ": " + ex.ToString());
                    errors++;
                }
            }

            return errors;
        }

        private static void Log(string logLine)
        {
            try
            {
                logLine = DateTime.Now.ToString("HH:mm") + ": " + logLine; 
                File.AppendAllLines(Configuration.LogPfad + "\\" + LogFile, new List<string>() { logLine });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to log: " + ex.ToString());
            }
        }

        //picturepark functions
        private static CoreInfo LoginPP(ExtendedPublicServiceClient pictureParkService)
        {
            try
            {
                int customerId = Configuration.PP_CustomerId;
                int contentLanguageId = 2;
                ApplicationLanguage applLang = ApplicationLanguage.English;
                string clientGuid = Configuration.PP_ClientGUID;
                coreInfo = pictureParkService.CreateSession(
                customerId,
                applLang,
                contentLanguageId,
                null,
                null,
                clientGuid,
                null,
                null);
                coreInfo.User = new User()
                {
                    Email = Configuration.PP_Email,
                    Password = Configuration.PP_Password,
                    Language = ApplicationLanguage.English
                };
                coreInfo = pictureParkService.Login(coreInfo);
            }
            catch (Exception ex)
            {
                Log("[ERROR] Picturepark Login fehlgeschlagen, Parameter oder Login falsch: " + ex.ToString());
                MessageBox.Show("Parameter oder Login falsch, mehr Informationen in der Logdatei", "Picturepark Login fehlgeschlagen");
                return null;
            }

            return coreInfo;
        }

        public static int GetFieldIdByName(List<Field> assetFields, string name)
        {
            return assetFields.SingleOrDefault(f => f.Name == name).FieldId;
        }
    }
}
