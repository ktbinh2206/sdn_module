using System.Diagnostics;
using System.IO.Compression;

namespace sdnmodule
{
    internal class Program
    {
        #region Constants
        const string sourceDirectory = @"D:\Studying Materials\LastDance\Capstone\Research\FerModulization\sdnprojects"; // Thư mục chứa các file ZIP
        const string extractedDirectory = @"D:\Studying Materials\LastDance\Capstone\Research\FerModulization\extracted_projects"; // Thư mục giải nén
        const string destinationProjectsPath = "/app"; // Thư mục trong container chứa project Node.js

        const string dockerExePath = @"C:\Program Files\Docker\Docker\Docker Desktop.exe"; // Đường dẫn đến Docker CLI

        const string networkName = "sdn-network"; // Tên network
        const string nodeContainerName = "node-env"; // Tên container chứa Node.js
        const string mongoContainerName = "mongo-env"; // Tên container chứa MongoDB

        const string localDatabasePath = @"D:\Studying Materials\LastDance\Capstone\Research\FerModulization\sdn-database"; // Folder where JSON files are stored
        const string destinationDatabasePath = "/sdn-database"; // Folder inside the container where JSON files are copied to
        const string databaseName = "PE_Fall2024_B5"; // Database name
        #endregion

        static void Main(string[] args)
        {
            ExtractZipFiles(sourceDirectory, extractedDirectory);
            RunDocker();
            if (Process.GetProcessesByName("Docker Desktop").Length > 0)
            {
                CreateNetWork(networkName);
                CreateMongoEnv(mongoContainerName, networkName, localDatabasePath, destinationDatabasePath);
                CreateNodeEnv(nodeContainerName, extractedDirectory, networkName, destinationProjectsPath);
                StartNodeProjects(destinationProjectsPath);
            }
        }

        //extract zip files
        static void ExtractZipFiles(string sourceDir, string targetDir)
        {
            //if target directory does not exist, create it
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            string[] zipFiles;
            try
            {
                //get all zip files in source directory
                zipFiles = Directory.GetFiles(sourceDir, "*.zip");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return;
            }
            // extract each zip file to corresponding sub-directory
            foreach (var zipFile in zipFiles)
            {
                string extractPath = Path.Combine(targetDir, Path.GetFileNameWithoutExtension(zipFile));

                // Check if the folder exists and delete it if necessary
                if (Directory.Exists(extractPath))
                {
                    // Recursively delete the folder and its contents
                    Directory.Delete(extractPath, true);
                }
                ZipFile.ExtractToDirectory(zipFile, extractPath);

                Console.WriteLine($"Extracted: {zipFile}");
            }
        }

        // Run Docker Desktop
        static void RunDocker()
        {
            if (IsDockerReady())
            {
                Console.WriteLine("Docker is ready.");
                return;
            }

            try
            {
                Process.Start(dockerExePath);
                Console.WriteLine("Docker is starting ...");

                // Check every second if Docker is ready, up to 10 seconds
                for (int i = 0; i < 10; i++)
                {
                    Thread.Sleep(1000);  // 1 second
                    if (IsDockerReady())
                    {
                        Console.WriteLine("Docker is ready.");
                        return;
                    }
                }

                Console.WriteLine("Docker did not start within 10 seconds.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting Docker: {ex.Message}");
            }
        }

        // Check if Docker is ready
        static bool IsDockerReady()
        {
            try
            {
                // Run "docker info" to check if Docker is ready
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = "info",
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                process.WaitForExit();
                return process.ExitCode == 0;  // Docker is ready if the command succeeds
            }
            catch
            {
                return false;  // Docker is not ready if an error occurs
            }
        }

        //create network for 2 containers
        private static void CreateNetWork(string networkName)
        {
            string networkInfo = RunCommand("docker", "network ls --filter name=" + networkName + " --quiet", "");
            if (string.IsNullOrEmpty(networkInfo))
            {
                RunCommand("docker", "network create " + networkName, "Created network.");
            }
            else
            {
                Console.WriteLine("Network already exists.");
            }
        }

        //check existed container
        private static bool CheckExistedContainer(string containerName)
        {
            string ContainerInfo = RunCommand("docker", "ps -a --filter name=" + containerName, "Current container");
            return !string.IsNullOrEmpty(ContainerInfo);
        }

        //create mongo env
        private static void CreateMongoEnv(string mongoContainerName, string networkName, string localDatabasePath, string destinationDatabasePath)
        {
            if (CheckExistedContainer(mongoContainerName))
            {
                //remove existing container
                RunCommand("docker", "stop " + mongoContainerName, "");
                RunCommand("docker", "rm -f " + mongoContainerName, "");
                Console.WriteLine("Removed mongo container.");
            }
            //run new container with volume mount
            RunCommand("docker", $"run -d --name {mongoContainerName} --network {networkName} -v \"{localDatabasePath}:/{destinationDatabasePath}\" -p 27017:27017 mongo", "Created mongo-env container.");
        }

        //import database 
        private static void ImportDatabase(string mongoContainerName, string databaseName, string localDatabasePath, string destinationDatabasePath)
        {
            // Get all JSON files in the folder
            string[] filePaths = Directory.GetFiles(localDatabasePath, "*.json");

            // Loop through each file and insert its contents into MongoDB
            foreach (string filePath in filePaths)
            {
                // Get the collection name (without extension)
                string collectionName = Path.GetFileNameWithoutExtension(filePath);

                // Use docker exec to run mongoimport inside the mongo-env container
                string command = $"docker exec -i {mongoContainerName} mongoimport --db {databaseName} --collection {collectionName} --file /{destinationDatabasePath}/{Path.GetFileName(filePath)} --jsonArray";
                RunCommand("cmd.exe", $"/C {command}", $"Importing {filePath} into MongoDB collection {collectionName}");
            }

            Console.WriteLine("All files have been successfully imported into MongoDB.");
        }

        //create node env
        private static void CreateNodeEnv(string nodeContainerName, string sourceDir, string networkName, string destinationProjectPath)
        {
            if (CheckExistedContainer(nodeContainerName))
            {
                //remove existing container
                RunCommand("docker", "stop " + nodeContainerName, "");
                RunCommand("docker", "rm -f " + nodeContainerName, "");
                Console.WriteLine("Removed node container.");
            }

            //run new container
            RunCommand("docker", "run -d --name " + nodeContainerName + " --network " + networkName + " -v \"" + sourceDir + ":" + destinationProjectPath + "\"  -p 3000:3000 -p 9000:9000 -it node:22", "Created node-env container.");
        }

        //Start node project
        static void StartNodeProjects(string destinationProjectsPath)
        {
            StartMultipleNodeProjects(destinationProjectsPath);
        }

        //Modify .env file in frontend
        static void ModifyFE(int backendPort, string projectPath)
        {
            RunCommand("docker", $"exec -d -it node-env /bin/sh -c \"cd {projectPath} && find ./build/static/js -type f -exec sed -i 's|http://localhost:9999|http://localhost:{backendPort}|g' {{}} +", "");
        }

        //Modify .env file in backend
        static void ModifyEnvBE(int port, string projectPath, string projectName)
        {
            
            RunCommand("docker", $"exec -it node-env /bin/sh -c \"cd {projectPath} && echo PORT={port} > .env && echo HOST_NAME={mongoContainerName} >> .env && echo MONGODB_URI=mongodb://{mongoContainerName}:27017 >> .env && echo DB_NAME={databaseName}_{projectName} >> .env\"", "Modified .env file");
        }

        //Run Command
        static string RunCommand(string cmd, string args, string logs)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = cmd,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = new Process())
            {
                process.StartInfo = startInfo;
                process.Start();

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (!string.IsNullOrEmpty(error))
                {
                    Console.WriteLine(error);
                }
                Console.WriteLine(output);
                Console.WriteLine(logs);

                return output;
            }
        }
        //Start multiple node projects
        static void StartMultipleNodeProjects(string destinationProjectsPath)
        {
            //install serve package
            RunCommand("docker", "exec -it node-env /bin/sh -c \"npm install -g serve\"", "Install necessary packages");

            // Get all project directories// Get all project directories inside the Docker container
            string listDirsCommand = $"exec -it node-env /bin/sh -c \"ls -d {destinationProjectsPath}/*/\"";
            string result = RunCommand("docker", listDirsCommand, "");

            // Split the result into an array of directories
            string[] projectDirs = result.Split(new[] { '\t','\r','\n' }, StringSplitOptions.RemoveEmptyEntries);


            int fePort = 3000;
            int bePort = 9000;

            foreach (var projectDir in projectDirs)
            {
                string projectName = Path.GetFileName(projectDir.TrimEnd('/'));
                string fePath = Path.Combine(projectDir, "front-end");
                string bePath = Path.Combine(projectDir, "back-end");

                // Modify and start front-end
                ModifyFE(bePort, fePath);
                RunCommand("docker", $"exec -d -it node-env /bin/sh -c \"cd {fePath} && serve -s build -l {fePort}\"", $"Started front-end for {projectName}");

                // Modify and start back-end
                ModifyEnvBE(bePort, bePath, projectName);
                ImportDatabase(mongoContainerName, databaseName + "_" + projectName, localDatabasePath, destinationDatabasePath);
                RunCommand("docker", $"exec -d -it node-env /bin/sh -c \"cd {bePath} && npm install && npm start\"", $"Started back-end project for {projectName}");

                fePort++;
                bePort++;
            }
        }
    }
}
