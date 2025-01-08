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

        static async Task Main(string[] args)
        {
            var extractTask = Task.Run(() => ExtractZipFiles(sourceDirectory, extractedDirectory));
            var dockerTask = Task.Run(() => RunDocker());
            await Task.WhenAll(extractTask, dockerTask);

            if (Process.GetProcessesByName("Docker Desktop").Length > 0)
            {
                var networkTask = Task.Run(() => CreateNetWork(networkName));
                await networkTask;

                var mongoTask = Task.Run(() => CreateMongoEnv(mongoContainerName, networkName, localDatabasePath, destinationDatabasePath));
                var nodeTask = Task.Run(() => CreateNodeEnv(nodeContainerName, extractedDirectory, networkName, destinationProjectsPath));
                await Task.WhenAll(mongoTask, nodeTask);


                await StartNodeProjects(destinationProjectsPath);
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
                Console.WriteLine("Source Directory doesn't contain zip file");
                Console.WriteLine($"Error: {ex.Message}");
                return;
            }

            // extract each zip file to corresponding sub-directory
            Parallel.ForEach(zipFiles, zipFile =>
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
            });

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
            var networkInfo = RunCommand("docker", "network ls --filter name=" + networkName + " --quiet", "");
            if (string.IsNullOrEmpty(networkInfo.Output))
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
            var containerInfo = RunCommand("docker", "ps -a --filter name=" + containerName, "Current container");
            return !string.IsNullOrEmpty(containerInfo.Output);
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
        static async Task StartNodeProjects(string destinationProjectsPath)
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

            RunCommand("docker", $"exec -it node-env /bin/sh -c \"cd {projectPath} && echo PORT={port} > .env && echo HOST_NAME={mongoContainerName} >> .env && echo MONGODB_URI=mongodb://{mongoContainerName}:27017 >> .env && echo DB_NAME={databaseName}_{projectName} >> .env && echo NODE_ENV=production  >> .env \"", "Modified .env file");
        }

        // Run Command
        public static (string Output, string Error) RunCommand(string cmd, string args, string logs)
        {
            try
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

                using (Process process = new Process { StartInfo = startInfo })
                {
                    Console.WriteLine($"[INFO] Executing command: {cmd} {args}");
                    Console.WriteLine($"[INFO] Logs: {logs}");

                    process.Start();

                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (!string.IsNullOrEmpty(error))
                    {
                        Console.WriteLine($"[ERROR] Command Error: {error}");
                    }

                    Console.WriteLine($"[OUTPUT] Command Output: {output}");
                    return (output, error);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EXCEPTION] An error occurred while executing the command: {ex.Message}");
                throw;
            }
        }

        //Start multiple node projects
        static void StartMultipleNodeProjects(string destinationProjectsPath)
        {
            // Start timing
            Stopwatch stopwatch = Stopwatch.StartNew();

            //install serve package
            RunCommand("docker", "exec -it node-env /bin/sh -c \"npm install -g serve pm2\"", "Install necessary packages");


            // Get all project directories// Get all project directories inside the Docker container


            string listDirsCommand = $"exec -it node-env /bin/sh -c \"ls -d {destinationProjectsPath}/*/\"";
            var result = RunCommand("docker", listDirsCommand, "").Output;

            // Split the result into an array of directories
            string[] projectDirs = result.Split(new[] { '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);


            int fePort = 2999;
            int bePort = 8999;

            Parallel.ForEach(projectDirs, projectDir =>
            {
                string projectName = Path.GetFileName(projectDir.TrimEnd('/'));
                string fePath = Path.Combine(projectDir, "front-end");
                string bePath = Path.Combine(projectDir, "back-end");

                // Thread-safe increment of ports
                int currentFePort = Interlocked.Increment(ref fePort);
                int currentBePort = Interlocked.Increment(ref bePort);

                // Modify and start front-end
                ModifyFE(currentBePort, fePath);
                RunCommand("docker", $"exec -d -it node-env /bin/sh -c \"cd {fePath} && serve -s build -l {currentFePort}\"", $"Start front-end for {projectName}");
                WaitForServiceReady(currentFePort, $"Front-end for {projectName}");

                // Modify and start back-end
                ModifyEnvBE(currentBePort, bePath, projectName);
                ImportDatabase(mongoContainerName, databaseName + "_" + projectName, localDatabasePath, destinationDatabasePath);
                RunCommand("docker", $"exec -it node-env /bin/sh -c \"cd {bePath} && npm ci\"", $"Install dependencies for back-end project {projectName}");
                RunCommand("docker", $"exec -d -it node-env /bin/sh -c \"cd {bePath} && pm2 start server.js --name {projectName} --watch\"", $"Start back-end project for {projectName}");

                WaitForServiceReady(currentBePort, $"Back-end for {projectName}");

                 Thread.Sleep(1000); // 500ms để giảm tải hệ thống
            });

            // Stop timing
            stopwatch.Stop();
            Console.WriteLine($"All projects are ready. Total time: {stopwatch.Elapsed.TotalSeconds} seconds.");
        }

        // Wait until a service is ready
        static void WaitForServiceReady(int port, string serviceName)
        {
            for (int i = 0; i < 1000; i++) // Check up to 30 times (30 seconds)
            {
                var result = RunCommand("docker", $"exec -it node-env /bin/sh -c \"curl -s -w \"%{{http_code}}\" -o /dev/null http://localhost:{port}\"", "").Output;
                if (result.Trim() != "000")
                {
                    Console.WriteLine($"{serviceName} is ready on port {port}.");
                    return;
                }
                Thread.Sleep(2000); 
            }

            Console.WriteLine($"{serviceName} did not become ready within the expected time.");
        }

    }

}