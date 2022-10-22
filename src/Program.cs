using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.NamingConventionBinder;
using System.CommandLine.Parsing;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;


namespace EAVFW.Extensions.GitHub.BlobStorageUploadArtifact
{
    [AttributeUsage(AttributeTargets.Property,AllowMultiple =true)]
    public class AliasAttribute : Attribute
    {
        public string Alias { get; }
        public AliasAttribute(string alias)
        {
            Alias = alias;
        }
    }
    public static class COmmandExtensions
    {
        public static Dictionary<string,Option> AddOptions(this Command command)
        {
            var o = new Dictionary<string, Option>();

            foreach (var prop in command.GetType().GetProperties())
            {
                var val = prop.GetValue(command);
                if (val is Option option)
                {
                    if(prop.GetCustomAttribute<RequiredAttribute>() is RequiredAttribute)
                    {
                        option.IsRequired = true;
                    }
                    command.Add(option);
                }else if (prop.GetCustomAttributes<AliasAttribute>().Any())
                {
                    var aliass = prop.GetCustomAttributes<AliasAttribute>();
                    var op= typeof(COmmandExtensions).GetMethod(nameof(CreateOption), 1, new[] { typeof(string), typeof(string) })
                        .MakeGenericMethod(prop.PropertyType).Invoke(null, new object[] { aliass.First().Alias, prop.GetCustomAttribute<DescriptionAttribute>().Description }) as Option;
                    foreach (var a in aliass.Skip(1))
                        op.AddAlias(a.Alias);
                    o[prop.Name] = op;

                    command.Add(op);
                }
            }

            return o;
        }
        public static Option<T> CreateOption<T>(string alias,string description)
        {
            return new Option<T>(alias, description);
        }

        public static ICommandHandler Create(Command cmd, IEnumerable<Command> commands, Func<ParseResult, IConsole, Task<int>> runner)
        {
            foreach (var command in commands)
                cmd.Add(command);

           var options= cmd.AddOptions();
           
            
            Task<int> Run(ParseResult parsed, IConsole console)
            {

                foreach(var o in options)
                {
                    cmd.GetType().GetProperty(o.Key).SetValue(cmd,parsed.GetValueForOption(o.Value));
                }

                return runner(parsed, console);
            }
           
            
            return CommandHandler.Create(Run);

            
        }

    }

    //public class UploadCommand : Command
    //{
    //    public UploadCommand() : base("Upload", "Upload artifacts")
    //    {
    //    }
    //}
    public class App : System.CommandLine.RootCommand
    {
        [Alias("--name")]
        [Alias("-n")]
        [Description("\"Staring with a slash / will use github-action-artifacts as the container name, otherwise the first segment is used as container name.\"")]
        public string NameOption { get; set; }// = new Option<string>("--name", );

        
        [Required]
        public Option<string> ConnectionString { get; } = new Option<string>("--connection-string", "connectionstring");

        [Alias("--path")]
        [Description("The path / glob pattern to upload")]
        public string Path { get; set; }

        public App(IEnumerable<Command> commands)
        { 
           
             
            Handler = COmmandExtensions.Create(this,commands, Run);

        }
        public async Task<int> Run(ParseResult parseResult, IConsole console)  
        {
            console.WriteLine("Hello World 2");

            var storage = new BlobServiceClient(ConnectionString.GetValue(parseResult));
            var name = NameOption.Replace("\\","/");
            var containerName = name.Split("/").First() ?? "github-action-artifacts";
        
            var basePath = string.Join("/", name.Split('/').Skip(1));
            var runid = Environment.GetEnvironmentVariable("GITHUB_RUN_ID");
            var destinationPath = $"artifacts/{basePath.Trim('/')}/runs/{runid}";


            if (containerName.Length < 3)
            {   
                destinationPath = $"{containerName}/artifacts/{basePath.Trim('/')}/runs/{runid}";
                containerName = "github-action-artifacts";
            }

            var container = storage.GetBlobContainerClient(containerName);
            await container.CreateIfNotExistsAsync();

          

            console.WriteLine($"Uploading to {destinationPath}");

            var currentFolder = Directory.GetCurrentDirectory().Replace("\\", "/");

            async Task<int> Upload(IEnumerable<string> paths)
            {
                
                if (paths.Skip(1).Any())
                {
                    //Upload Many Files

                    //var ms = new MemoryStream();
                    var target = container.GetBlobClient(destinationPath + ".zip");

                    using (var archive = new ZipArchive(await target.OpenWriteAsync(true), ZipArchiveMode.Create))
                    {
                        foreach (var path in paths) {
                            var entry = archive.CreateEntry(path.Substring(currentFolder.Length + 1));

                            using (var w = entry.Open())
                            {
                                await File.OpenRead(path).CopyToAsync(w);
                            }
                            
                        }
                    }
                     
                        
                    return 0;
                }

                {
                    // Upload Single File
                    var path = paths.Single().Replace("\\", "/");

                    var blob = container.GetBlobClient(destinationPath+"/"+ path.Substring(currentFolder.Length + 1));

                    await blob.UploadAsync(path);
                }
                return 0;
            }
         
            var path = Path.Replace("\\", "/");
            console.WriteLine($"Finding files for {path}");
            if (path.Contains("*"))
            {

                var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
                matcher.AddInclude(Path);

                var files = matcher.GetResultsInFullPath(Directory.GetCurrentDirectory());

                return await Upload(files.Select(c => c.Replace("\\", "/")).ToArray());

                
              
            }

            FileAttributes attr = File.GetAttributes(path);

            
            if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
            {
                //Folder
                return await Upload(Directory.GetFiles(path).Select(c => c.Replace("\\", "/")).ToArray());
            }

            //File


            return await Upload(new[] { path});



            
        }
        
    }
    internal sealed class ConsoleHostedService<TApp> : IHostedService where TApp:RootCommand
    {
        private readonly IHostApplicationLifetime appLifetime;
        private readonly TApp app;
        public int Result = 0;
        public ConsoleHostedService(
            IHostApplicationLifetime appLifetime,
            IServiceProvider serviceProvider,
            TApp app)
        {
            this.appLifetime = appLifetime;
            this.app = app;
            //...
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
    return             app.InvokeAsync(System.Environment.GetCommandLineArgs().Skip(1).ToArray())
                .ContinueWith(result =>
                {
                    Result = result.Result;
                    appLifetime.StopApplication();
                    
                });
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
           
            return Task.CompletedTask;
        }
    }

    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            

            using IHost host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((_, services) => services
                .AddSingleton<ConsoleHostedService<App>>()
                .AddHostedService(sp=>sp.GetRequiredService<ConsoleHostedService<App>>())
                .AddSingleton<App>()
               // .AddSingleton<Command, UploadCommand>())
                ).Build();

            var app = host.Services.GetRequiredService<ConsoleHostedService<App>>();
            await host.RunAsync();
            return app.Result; 

        }
    }
}