using CsvHelper;
using CsvHelper.Configuration;
using ENZO.Extensions;
using EnzoApi.Models;
using EnzoCommanderSDK;
using EnzoCommanderSDK.mef;
using EnzoCommanderSDK.Structures.interfaces;
using System.Globalization;
using System.Security.Cryptography;
//using System.Data.Common;
//using System.Data.OleDb;

namespace EnzoApi.Extensionsf
{
    public static class EnzoEcommerce
    {

#if ISLOCAL
        static string _ROOT = Path.GetDirectoryName(".\\") ?? "";
#else
        static string _ROOT = Path.GetDirectoryName(Environment.ProcessPath);
#endif

        static string _DownloadRoot = $"{_ROOT}\\downloads\\";
        static string _UploadRoot = $"{_ROOT}\\uploads\\";


        #region Function Codex
        public static Dictionary<string, Delegate> _codex = new Dictionary<string, Delegate>()
        {
            {"orders", GenerateOrdersFiles }
        };

        #endregion


        public static WebApplicationBuilder? AddEnzoSDK(this WebApplicationBuilder builder)
        {
            if (builder == null) return builder;

            EnzoApiConfig config = new EnzoApiConfig();

            builder.Configuration.Bind("EnzoApiConfig", config);


            var services = builder.Services;

            var _commander = new CommandComposer(config.EnzoCom, _ROOT, _UploadRoot);
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


            app.MapGet("api/v1/orders", async (HttpRequest request, HttpContext ctx, CommandComposer commander, Gateway gateway, string? action) =>
            {
                Response rsl = new Response();
                try
                {

                    if (!string.IsNullOrEmpty(action)) //if action is null endpoint will get and send, otherwise g = get and s = send.
                    {
                        switch (action)
                        {
                            case "s":
                                goto SEND;
                            case "g":
                                goto GET;
                            default:

                                var resp = ctx.Response;

                                resp.StatusCode = StatusCodes.Status400BadRequest;

                                return rsl;
                        }
                    }


                GET:
                    //Get the files:

                    rsl.Success = await commander.cmd.GetFile("orders");

                    if (!rsl.Success)
                    {
                        rsl.Msg = "Unable to get orders.";
                        goto EXIT;
                    }


                    //Commit orders to database:
                    rsl.Success = await CommitOrders(gateway);

                    if (string.IsNullOrEmpty(action) || !action.Contains("s", StringComparison.OrdinalIgnoreCase))
                    {
                        goto EXIT;
                    }

                SEND:
                    if (await GenerateOrdersFiles(gateway))
                    {
                        rsl.Success = await commander.cmd.SendFile("orders");

                        if (!rsl.Success) rsl.Msg = "Unable to send orders.";
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
            EXIT:
                return rsl;

            });

            app.MapPost("api/v1/orders", async (CommandComposer commander, Gateway gateway) =>
            {


                var orders = await gateway.GetOrdersAsync();

                if (!Directory.Exists(_UploadRoot)) Directory.CreateDirectory(_UploadRoot);

                string stamp = $"{DateTime.Now.ToString("yyyyddMMHHmmss")}.csv";
                string ohFile = $"orders_{stamp}";
                string detFile = $"order-items_{stamp}";

                var orderHeaders = orders.Select(oh => (OrderHeader)oh).ToList();

                //write order header file:
                using (var writer = new StreamWriter($"{_UploadRoot}{ohFile}"))
                {
                    try
                    {
                        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
                        {
                            csv.WriteRecords(orderHeaders);
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("Commerce API - Orders (POST)", ex);
                    }
                    finally
                    {
                        writer.Close();
                    }

                }

                //write order details file:
                List<OrderItem> details = new List<OrderItem>();

                orders.ForEach(oi => details.AddRange(oi.Detail.ToArray()));

                using (var writer = new StreamWriter($"{_UploadRoot}{detFile}"))
                {
                    try
                    {
                        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
                        {
                            csv.WriteRecords(details);
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("Commerce API - Orders (POST)", ex);
                    }
                    finally
                    {
                        writer.Close();
                    }

                }

                //Send the files:

                await commander.cmd.SendFile("orders");


                return true;

            });

            app.MapGet("api/v1/customers", async (HttpRequest request, HttpContext ctx, CommandComposer commander, Gateway gateway, string? cmd) =>
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

            app.MapGet("api/v1/interact", async (HttpContext ctx, CommandComposer commander, Gateway gateway, string cmd, string? action) =>
            {
                Response rsp = new Response();

                var resp = ctx.Response;

                if (string.IsNullOrEmpty(cmd))
                {

                    resp.StatusCode = StatusCodes.Status400BadRequest;

                    rsp.Msg = "cmd parameter must be populated!";
                    return rsp;
                }

                if (string.IsNullOrEmpty(action))
                {
                    //Do Send and get:
                    bool rsl = await commander.cmd.GetFile(cmd);



                    if (_codex.ContainsKey(cmd))
                    {

                        var deleg = _codex[cmd];

                        //deleg.DynamicInvoke(commander, gateway);

                        //Generate file:
                        await GenerateCsv(cmd, commander, gateway);


                        //switch (cmd)
                        //{
                        //    case "orders":
                        //        //var orders = await gateway.GetOrdersAsync();
                        //        //if (orders.Any()) await commander.cmd.GenerateFileAsync(orders, cmd);


                        //        break;
                        //    case "customers":
                        //        //No action yet, just send any customers.csv file found in the upload folder...

                        //        break;
                        //    case "contacts":
                        //        //No action yet, just end any contacts.csv file found in the upload folder...

                        //        break;
                        //    default:
                        //        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;

                        //        return rsp;
                        //}

                        rsl = await commander.cmd.SendFile(cmd);

                    }
                }
                else
                {
                    switch (action)
                    {
                        case "s":

                            await GenerateCsv(cmd, commander, gateway);

                            rsp.Success = await commander.cmd.SendFile(cmd);
                            break;
                        case "g":

                            if (_codex.ContainsKey(cmd))
                            {

                                var deleg = _codex[cmd];

                                deleg.DynamicInvoke(commander, gateway);

                                rsp.Success = await commander.cmd.SendFile(cmd);

                            }

                            rsp.Success = await commander.cmd.GetFile(cmd);
                            break;

                        default:

                            rsp.Msg = "Unknown action";
                            resp.StatusCode = StatusCodes.Status400BadRequest;
                            break;
                    }
                }

                return rsp;
            });

            app.MapGet("api/model", async (HttpContext ctx) =>
            {
                return new List<Order>();
            });

            app.MapGet("api/test", async (Gateway gateway) =>
            {
                await gateway.GetCustomersAsync();


                return;
            });

            return app;
        }



        static async Task<bool> GenerateCsv(string mapping, CommandComposer commander, Gateway gateway)
        {
            bool rsl = false;

            switch (mapping)
            {
                case "orders":
                    var orders = await gateway.GetOrdersAsync();
                    if (orders.Any()) rsl = await commander.cmd.GenerateFileAsync(orders, mapping);


                    break;
                default:

                    return true;
            }

            return rsl;
        }

        /// <summary>
        /// Generate the orders and order-items CSV file.
        /// </summary>
        /// <param name="gateway"></param>
        /// <param name="commander"></param>
        /// <exception cref="Exception"></exception>
        static async Task<bool> GenerateOrdersFiles(Gateway gateway)//, CommandComposer commander)
        {


            var orders = await gateway.GetOrdersAsync();

            if (!Directory.Exists(_UploadRoot)) Directory.CreateDirectory(_UploadRoot);

            string stamp = $"{DateTime.Now.ToString("yyyyddMMHHmmss")}.csv";
            string ohFile = $"orders_{stamp}";
            string detFile = $"order-items_{stamp}";

            var orderHeaders = orders.Select(oh => (OrderHeader)oh).ToList();

            //write order header file:
            using (var writer = new StreamWriter($"{_UploadRoot}{ohFile}"))
            {
                try
                {
                    using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
                    {
                        csv.WriteRecords(orderHeaders);
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception("Commerce API - Orders (POST)", ex);
                }
                finally
                {
                    writer.Close();
                }

            }

            //write order details file:
            List<OrderItem> details = new List<OrderItem>();

            orders.ForEach(oi => details.AddRange(oi.Detail.ToArray()));

            using (var writer = new StreamWriter($"{_UploadRoot}{detFile}"))
            {
                try
                {
                    using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
                    {
                        csv.WriteRecords(details);
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception("Commerce API - Orders (POST)", ex);
                }
                finally
                {
                    writer.Close();
                }

            }


            return true;




            //var orders = await gateway.GetOrdersAsync();

            //if (!Directory.Exists(_UploadRoot)) Directory.CreateDirectory(_UploadRoot);

            //string stamp = $"{DateTime.Now.ToString("yyyyddMMHHmmss")}.csv";
            //string ohFile = $"orders_{stamp}";
            //string detFile = $"order-items_{stamp}";

            //var orderHeaders = orders.Select(oh => (OrderHeader)oh).ToList();

            ////write order header file:
            //using (var writer = new StreamWriter($"{_UploadRoot}{ohFile}"))
            //{
            //    try
            //    {
            //        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
            //        {
            //            csv.WriteRecords(orderHeaders);
            //        }
            //    }
            //    catch (Exception ex)
            //    {
            //        throw new Exception("Commerce API - Orders (POST)", ex);
            //    }
            //    finally
            //    {
            //        writer.Close();
            //    }

            //}

            ////write order details file:
            //List<OrderItem> details = new List<OrderItem>();

            //orders.ForEach(oi => details.AddRange(oi.Detail.ToArray()));

            //using (var writer = new StreamWriter($"{_UploadRoot}{detFile}"))
            //{
            //    try
            //    {
            //        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
            //        {
            //            csv.WriteRecords(details);
            //        }
            //    }
            //    catch (Exception ex)
            //    {
            //        throw new Exception("Commerce API - Orders (POST)", ex);
            //    }
            //    finally
            //    {
            //        writer.Close();
            //    }

            //}

        }

        static async Task<bool> CommitOrders(Gateway gateway)
        {

            List<OrderHeader> orderHeaders = new List<OrderHeader>();
            List<OrderItem> orderDetail = new List<OrderItem>();
            bool rsl = false;

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
                                oi.LoadFileName = Path.GetFileName(file);
                                oi.LoadFileDate = stamp;


                            });

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
                                oi.LoadFileName = Path.GetFileName(file);
                                oi.LoadFileDate = stamp;


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
                rsl = await gateway.AddOrdersAsync(orderHeaders, orderDetail);
            }



            return rsl;
        }


    }
}
