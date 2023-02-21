using CsvHelper;
using CsvHelper.Configuration;
using EnzoApi.Models;
using EnzoCommanderSDK;
using EnzoCommanderSDK;
using System.Globalization;
//using System.Data.Common;
//using System.Data.OleDb;

namespace EnzoApi.Extensionsf
{
    public static class EnzoEcommerce
    {
#if ISLOCAL
        static string _ROOT = Path.GetDirectoryName(Environment.ProcessPath) ?? "";
#else
        static string _ROOT = Path.GetDirectoryName(Environment.ProcessPath);
#endif

        static string _DownloadRoot = $"{_ROOT}\\downloads\\";
        static string _UploadRoot = $"{_ROOT}\\uploads\\";
        public static WebApplicationBuilder? AddEnzoSDK(this WebApplicationBuilder builder)
        {
            if (builder == null) return builder;

            EnzoApiConfig config = new EnzoApiConfig();

            builder.Configuration.Bind("EnzoApiConfig", config);


            var services = builder.Services;

            var _commander = new CommandComposer(config.EnzoCom, _ROOT);
            var gw = new Gateway(config.DBConn);


            _commander.cmd.OnHandshakeFailed += async (s, e) =>
            {
                //Log error on Database...
                await gw.LogErrorAsync(e);
            };

            services.AddSingleton<CommandComposer>(_commander)
                    .AddSingleton<Gateway>(gw);



            return builder;
        }
        public static WebApplication? UseEnzoEcommerce(this WebApplication? app)
        {

            if (app == null) return app;


            app.MapGet("api/v1/orders", async (HttpRequest request, HttpContext ctx, CommandComposer commander, Gateway gateway) =>
            {
                Response rsl = new Response();
                try
                {
                    //Get the files:

                    rsl.Success = await commander.cmd.GetFile("orders");

                    List<OrderHeader> orderHeaders = new List<OrderHeader>();
                    List<OrderItem> orderDetail = new List<OrderItem>();

                    //commander.cmd.OnHandshakeFailed += async (s, e) =>
                    //{
                    //    //Log error on Database...
                    //    await gateway.LogErrorAsync(e);
                    //};


                    //List<Order> orders = new List<Order>();
                    //if(!Directory.Exists(_DownloadRoot)) Directory.CreateDirectory(_DownloadRoot);

                    if (rsl.Success)
                    {
                        //Parse Order Headers first:
                        var files = Directory.GetFiles(_DownloadRoot, "*orders_*.csv").ToList();

                        if (files.Any())
                        {
                            //List<OrderHeader> orderHeaders = new List<OrderHeader>();

                            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                            {
                                HasHeaderRecord = true,
                                Delimiter = ",",
                            };


                            foreach (var file in files)
                            {
                                using (StreamReader reader = new StreamReader(File.OpenRead(file)))
                                {
                                    CsvReader csv = new CsvReader(reader, config);

                                    var ohdr = csv.GetRecords<OrderHeader>();

                                    var tempList = ohdr.ToList();
                                    if (tempList.Any())
                                    {
                                        var fileNameParsed = Path.GetFileName(file).Split('_');
                                        string fileStamp = fileNameParsed[fileNameParsed.Length - 1].Replace(".csv", "");
                                        DateTime stamp = DateTime.Parse(string.Format("{2}/{1}/{0} {3}:{4}", fileStamp.Substring(0, 4),
                                                                                                      fileStamp.Substring(4, 2),
                                                                                                      fileStamp.Substring(6, 2),
                                                                                                      fileStamp.Substring(8, 2),
                                                                                                      fileStamp.Substring(10, 2))
                                                                        );

                                        tempList.ForEach(oi =>
                                        {
                                            oi.FileName = Path.GetFileName(file);
                                            oi.FileStamp = stamp;


                                        });

                                        //tempList.ForEach(o =>
                                        //{
                                        //    o.FileName = Path.GetFileName(file);
                                        //});

                                        orderHeaders.AddRange(tempList);
                                    }

                                }

                                if (!Directory.Exists($"{_DownloadRoot}\\archived")) Directory.CreateDirectory($"{_DownloadRoot}\\archived");
                                File.Move(file, $"{_DownloadRoot}\\archived\\{Path.GetFileName(file)}", true);



                            }

                        }

                        //Get Order Details now:
                        files = Directory.GetFiles(_DownloadRoot, "*orders-items_*.csv").ToList();
                        if (files.Any())
                        {
                            //orderDetail = new List<OrderItem>();



                            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                            {
                                HasHeaderRecord = true,
                                Delimiter = ",",
                            };


                            foreach (var file in files)
                            {
                                using (StreamReader reader = new StreamReader(File.OpenRead(file)))
                                {
                                    CsvReader csv = new CsvReader(reader, config);
                                    var detail = csv.GetRecords<OrderItem>();

                                    var tempList = detail.ToList();
                                    if (tempList.Any())
                                    {
                                        //orderDetail.AddRange(tempList);
                                        var fileNameParsed = Path.GetFileName(file).Split('_');
                                        string fileStamp = fileNameParsed[fileNameParsed.Length - 1].Replace(".csv", "");
                                        DateTime stamp = DateTime.Parse(string.Format("{2}/{1}/{0} {3}:{4}", fileStamp.Substring(0, 4),
                                                                                                      fileStamp.Substring(4, 2),
                                                                                                      fileStamp.Substring(6, 2),
                                                                                                      fileStamp.Substring(8, 2),
                                                                                                      fileStamp.Substring(10, 2))
                                                                        );

                                        tempList.ForEach(oi =>
                                        {
                                            oi.FileName = Path.GetFileName(file);
                                            oi.FileStamp = stamp;


                                        });
                                        orderDetail.AddRange(tempList);
                                        //var odr = orders.Where(order => order.Header.OrderNumber == tempList.First().OrderNumber).FirstOrDefault();  //orderDetail.AddRange(detail.ToList());

                                        //odr.Detail = tempList;
                                    }
                                }

                                //Archive file:
                                if (!Directory.Exists($"{_DownloadRoot}\\archived")) Directory.CreateDirectory($"{_DownloadRoot}\\archived");
                                File.Move(file, $"{_DownloadRoot}\\archived\\{Path.GetFileName(file)}");
                            }
                        }

                        if (orderHeaders.Any())
                        {

                            //Pass orders as JSON:
                            rsl.Success = await gateway.AddOrdersAsync(orderHeaders, orderDetail);

                            rsl.Msg = rsl.Success ? "Order import successful!" : "Order import failed!";
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception("Commerce API - Orders", ex);
                }
                finally
                {
                    commander.cmd.OnHandshakeFailed -= (s, e) => { };
                }

                return rsl;

            });

            app.MapPost("api/v1/orders", async () =>
            {




            });

            app.MapGet("api/v1/customers", async (HttpRequest request, HttpContext ctx, CommandComposer commander,Gateway gateway, string? cmd) =>
            {
                bool rsl = false;

                //commander.cmd.OnHandshakeFailed += async (s, e) =>
                //{
                //    //Log error on Database...
                //    await gateway.LogErrorAsync(e);
                //};

                try
                {
                    if (string.IsNullOrEmpty(cmd))
                    {
                        rsl = await commander.cmd.GetFile("customers");

                    }
                    else
                    {
                        switch (cmd)
                        {
                            case "s":
                                rsl = await commander.cmd.SendFile("customers");
                                break;
                            case "a":
                                rsl = await commander.cmd.GetFile("customers");
                                if (!rsl)
                                {
                                    rsl = await commander.cmd.SendFile("customers");
                                }
                                break;
                            default:
                                rsl = await commander.cmd.GetFile("customers");
                                break;
                        }
                    }

                }
                catch (Exception ex)
                {
                    throw new Exception("Commerce API - Customers", ex);
                }

                return rsl;
            });


            app.MapGet("api/model", async (HttpContext ctx) =>
            {
                return new List<Order>();
            });

            return app;
        }


        //static OleDbConnection ConnectionBuilder(string fileDir)
        //{
        //    return new OleDbConnection($"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={fileDir};Extended Properties=\"text;HDR=Yes;FMT=Delimited\";");
        //}
    }
}
