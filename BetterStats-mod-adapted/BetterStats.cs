using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Globalization;
using System.Linq;
using System.Text;
using DefaultNamespace;
using BepInEx.Logging;
using Newtonsoft.Json;
using System.IO;
using Amazon;
using Amazon.S3;
using Amazon.S3.Transfer;
using Amazon.S3.Model;
using System.Threading.Tasks;

namespace BetterStats.ExtractData.Sammy
{
    [BepInPlugin("com.brokenmass.plugin.DSP.BetterStats", PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class BetterStats : BaseUnityPlugin
    {
        private static Dictionary<int, ProductMetrics> counter = new Dictionary<int, ProductMetrics>();

        internal void Awake()
        {        
            Log = Logger;
        }

        public class ProductMetrics
        {
            public ItemProto itemProto;
            public Metrics metrics;
        }

        public class Metrics
        {
            public string Product;
            public string Planet;
            public string Star;
            public int production_actual = 0;
            public int consumption_actual = 0;
            public int production_theoretical = 0;
            public int consumption_theoretical = 0;
            public int producers = 0;
            public int consumers = 0;
            public long game_time_elapsed = 0;
            public string unique_game_identifier;
            public string Product_Planet_elapsed;
        }

        private static void EnsureId(ref Dictionary<int, ProductMetrics> dict, int id)
        {
            if (!dict.ContainsKey(id))
            {
                ItemProto itemProto = LDB.items.Select(id);

                dict.Add(id, new ProductMetrics()
                {
                    itemProto = itemProto
                });
                dict[id].metrics = new Metrics();
            }
        }

        // speed of fastest belt(mk3 belt) is 1800 items per minute
        public const float BELT_MAX_ITEMS_PER_MINUTE = 1800;
        public const float TICKS_PER_SEC = 60.0f;
        private const float RAY_RECEIVER_GRAVITON_LENS_CONSUMPTION_RATE_PER_MIN = 0.1f;

        public static void AddPlanetFactoryData(PlanetFactory planetFactory)
        {
            var factorySystem = planetFactory.factorySystem;
            var transport = planetFactory.transport;
            var veinPool = planetFactory.planet.factory.veinPool;
            var miningSpeedScale = (double)GameMain.history.miningSpeedScale;
            var maxProductivityIncrease = ResearchTechHelper.GetMaxProductivityIncrease();
            var maxSpeedIncrease = ResearchTechHelper.GetMaxSpeedIncrease();

            for (int i = 1; i < factorySystem.minerCursor; i++)
            {
                var miner = factorySystem.minerPool[i];
                if (i != miner.id) continue;

                var productId = miner.productId;
                var veinId = (miner.veinCount != 0) ? miner.veins[miner.currentVeinIndex] : 0;

                if (miner.type == EMinerType.Water)
                {
                    productId = planetFactory.planet.waterItemId;
                }
                else if (productId == 0)
                {
                    productId = veinPool[veinId].productId;
                }

                if (productId == 0) continue;


                EnsureId(ref counter, productId);

                float frequency = 60f / (float)((double)miner.period / 600000.0);
                float speed = (float)(0.0001 * (double)miner.speed * miningSpeedScale);

                float production = 0f;
                if (factorySystem.minerPool[i].type == EMinerType.Water)
                {
                    production = frequency * speed;
                }
                if (factorySystem.minerPool[i].type == EMinerType.Oil)
                {
                    production = frequency * speed * (float)((double)veinPool[veinId].amount * (double)VeinData.oilSpeedMultiplier);
                }

                // flag to tell us if it's one of the advanced miners they added in the 20-Jan-2022 release
                var isAdvancedMiner = false;
                if (factorySystem.minerPool[i].type == EMinerType.Vein)
                {
                    production = frequency * speed * miner.veinCount;
                    var minerEntity = factorySystem.factory.entityPool[miner.entityId];
                    isAdvancedMiner = minerEntity.stationId > 0 && minerEntity.minerId > 0;
                }

                // advanced miners aren't limited by belts
                if (!isAdvancedMiner)
                {
                    production = Math.Min(BELT_MAX_ITEMS_PER_MINUTE, production);
                }

                counter[productId].metrics.production_theoretical += Convert.ToInt32(production);
                counter[productId].metrics.producers++;
            }
            for (int i = 1; i < factorySystem.assemblerCursor; i++)
            {
                var assembler = factorySystem.assemblerPool[i];
                if (assembler.id != i || assembler.recipeId == 0) continue;

                var frequency = 60f / (float)((double)assembler.timeSpend / 600000.0);
                var speed = (float)(0.0001 * Math.Max(assembler.speedOverride, assembler.speed));

                // forceAccMode is true when Production Speedup is selected
                if (assembler.forceAccMode)
                {
                    speed += speed * maxSpeedIncrease;
                }
                else
                {
                    frequency += frequency * maxProductivityIncrease;
                }

                for (int j = 0; j < assembler.requires.Length; j++)
                {
                    var productId = assembler.requires[j];
                    EnsureId(ref counter, productId);

                    counter[productId].metrics.consumption_theoretical += Convert.ToInt32(frequency * speed * assembler.requireCounts[j]);
                    counter[productId].metrics.consumers++;
                }

                for (int j = 0; j < assembler.products.Length; j++)
                {
                    var productId = assembler.products[j];
                    EnsureId(ref counter, productId);

                    counter[productId].metrics.production_theoretical += Convert.ToInt32(frequency * speed * assembler.productCounts[j]);
                    counter[productId].metrics.producers++;
                }
            }
            for (int i = 1; i < factorySystem.fractionateCursor; i++)
            {
                var fractionator = factorySystem.fractionatePool[i];
                if (fractionator.id != i) continue;

                if (fractionator.fluidId != 0)
                {
                    var productId = fractionator.fluidId;
                    EnsureId(ref counter, productId);

                    counter[productId].metrics.consumption_theoretical += Convert.ToInt32(60f * 30f * fractionator.produceProb);
                    counter[productId].metrics.consumers++;
                }
                if (fractionator.productId != 0)
                {
                    var productId = fractionator.productId;
                    EnsureId(ref counter, productId);

                    counter[productId].metrics.production_theoretical += Convert.ToInt32(60f * 30f * fractionator.produceProb);
                    counter[productId].metrics.producers++;
                }

            }
            for (int i = 1; i < factorySystem.ejectorCursor; i++)
            {
                var ejector = factorySystem.ejectorPool[i];
                if (ejector.id != i) continue;

                EnsureId(ref counter, ejector.bulletId);

                counter[ejector.bulletId].metrics.consumption_theoretical += Convert.ToInt32(60f / (float)(ejector.chargeSpend + ejector.coldSpend) * 600000f);
                counter[ejector.bulletId].metrics.consumers++;
            }
            for (int i = 1; i < factorySystem.siloCursor; i++)
            {
                var silo = factorySystem.siloPool[i];
                if (silo.id != i) continue;

                EnsureId(ref counter, silo.bulletId);

                counter[silo.bulletId].metrics.consumption_theoretical += Convert.ToInt32(60f / (float)(silo.chargeSpend + silo.coldSpend) * 600000f);
                counter[silo.bulletId].metrics.consumers++;
            }

            for (int i = 1; i < factorySystem.labCursor; i++)
            {
                var lab = factorySystem.labPool[i];
                if (lab.id != i) continue;
                // lab timeSpend is in game ticks, here we are figuring out the same number shown in lab window, example: 2.5 / m
                // when we are in Production Speedup mode `speedOverride` is juiced. Otherwise we need to bump the frequency to account
                // for the extra product produced after `extraTimeSpend` game ticks
                var labSpeed = lab.forceAccMode ? (int)(lab.speed * (1.0 + maxSpeedIncrease) + 0.1) : lab.speed;
                float frequency = (float)(1f / (lab.timeSpend / GameMain.tickPerSec / (60f * labSpeed)));

                if (!lab.forceAccMode)
                {
                    frequency += frequency * maxProductivityIncrease;
                }

                if (lab.matrixMode)
                {
                    for (int j = 0; j < lab.requires.Length; j++)
                    {
                        var productId = lab.requires[j];
                        EnsureId(ref counter, productId);

                        counter[productId].metrics.consumption_theoretical += Convert.ToInt32(frequency * lab.requireCounts[j]);
                        counter[productId].metrics.consumers++;
                    }

                    for (int j = 0; j < lab.products.Length; j++)
                    {
                        var productId = lab.products[j];
                        EnsureId(ref counter, productId);

                        counter[productId].metrics.production_theoretical += Convert.ToInt32(frequency * lab.productCounts[j]);
                        counter[productId].metrics.producers++;
                    }
                }
                else if (lab.researchMode && lab.techId > 0)
                {
                    // In this mode we can't just use lab.timeSpend to figure out how long it takes to consume 1 item (usually a cube)
                    // So, we figure out how many hashes a single cube represents and use the research mode research speed to come up with what is basically a research rate
                    var techProto = LDB.techs.Select(lab.techId);
                    if (techProto == null)
                        continue;
                    TechState techState = GameMain.history.TechState(techProto.ID);
                    for (int index = 0; index < techProto.itemArray.Length; ++index)
                    {
                        var item = techProto.Items[index];
                        var cubesNeeded = techProto.GetHashNeeded(techState.curLevel) * techProto.ItemPoints[index] / 3600L;
                        var researchRate = GameMain.history.techSpeed * 60.0f;
                        var hashesPerCube = (float) techState.hashNeeded / cubesNeeded;
                        var researchFreq = hashesPerCube / researchRate;
                        EnsureId(ref counter, item);
                        counter[item].metrics.consumers++;
                        counter[item].metrics.consumption_theoretical += Convert.ToInt32(researchFreq * GameMain.history.techSpeed);
                    }
                }
            }
            double gasTotalHeat = planetFactory.planet.gasTotalHeat;
            var collectorsWorkCost = transport.collectorsWorkCost;
            for (int i = 1; i < transport.stationCursor; i++)
            {
                var station = transport.stationPool[i];
                if (station == null || station.id != i || !station.isCollector) continue;

                float collectSpeedRate = (gasTotalHeat - collectorsWorkCost > 0.0) ? ((float)((miningSpeedScale * gasTotalHeat - collectorsWorkCost) / (gasTotalHeat - collectorsWorkCost))) : 1f;

                for (int j = 0; j < station.collectionIds.Length; j++)
                {
                    var productId = station.collectionIds[j];
                    EnsureId(ref counter, productId);

                    counter[productId].metrics.production_theoretical += Convert.ToInt32(60f * TICKS_PER_SEC * station.collectionPerTick[j] * collectSpeedRate);
                    counter[productId].metrics.producers++;
                }
            }
            for (int i = 1; i < planetFactory.powerSystem.genCursor; i++)
            {
                var generator = planetFactory.powerSystem.genPool[i];
                if (generator.id != i)
                {
                    continue;
                }
                var isFuelConsumer = generator.fuelHeat > 0 && generator.fuelId > 0 && generator.productId == 0;
                if ((generator.productId == 0 || generator.productHeat == 0) && !isFuelConsumer)
                {
                    continue;
                }

                if (isFuelConsumer)
                {
                    // account for fuel consumption by power generator
                    var productId = generator.fuelId;
                    EnsureId(ref counter, productId);

                    counter[productId].metrics.consumption_theoretical += Convert.ToInt32(60.0f * TICKS_PER_SEC * generator.useFuelPerTick / generator.fuelHeat);
                    counter[productId].metrics.consumers++;
                }
                else
                {
                    var productId = generator.productId;
                    EnsureId(ref counter, productId);

                    counter[productId].metrics.production_theoretical += Convert.ToInt32(60.0f * TICKS_PER_SEC * generator.capacityCurrentTick / generator.productHeat);
                    counter[productId].metrics.producers++;
                    if (generator.catalystId > 0)
                    {
                        // account for consumption of critical photons by ray receivers
                        EnsureId(ref counter, generator.catalystId);
                        counter[generator.catalystId].metrics.consumption_theoretical += Convert.ToInt32(RAY_RECEIVER_GRAVITON_LENS_CONSUMPTION_RATE_PER_MIN);
                        counter[generator.catalystId].metrics.consumers++;
                    }
                }
            }
        }
        internal static ManualLogSource Log;
        public long timeCurrent;
        public long timePrev;
        List<Metrics> jsonList = new List<Metrics>();

        class UploadFileMPUHighLevelAPITest
        {
            public const string bucketName = "final-project.dsp.json-file";
            public const string keyName = "DSP_json.json";
            public const string filePath = "D://DSP_json.json";
            // Specify your bucket region (an example region is shown).
            public static readonly RegionEndpoint bucketRegion = RegionEndpoint.USEast1;
            public static IAmazonS3 s3Client;

            //public static void Main()
            //{
            //    s3Client = new AmazonS3Client(bucketRegion);
            //    UploadFileAsync().Wait();
            //}

            public static async Task UploadFileAsync()
            {
                try
                {
                    var fileTransferUtility =
                        new TransferUtility(s3Client);

                    //// Option 1. Upload a file. The file name is used as the object key name.
                    //await fileTransferUtility.UploadAsync(filePath, bucketName);
                    //Console.WriteLine("Upload 1 completed");

                    // Option 2. Specify object key name explicitly.
                    await fileTransferUtility.UploadAsync(filePath, bucketName, keyName);
                    //Console.WriteLine("Upload 2 completed");

                    //// Option 3. Upload data from a type of System.IO.Stream.
                    //using (var fileToUpload =
                    //    new FileStream(filePath, FileMode.Open, FileAccess.Read))
                    //{
                    //    await fileTransferUtility.UploadAsync(fileToUpload,
                    //                               bucketName, keyName);
                    //}
                    //Console.WriteLine("Upload 3 completed");

                    //// Option 4. Specify advanced settings.
                    //var fileTransferUtilityRequest = new TransferUtilityUploadRequest
                    //{
                    //    BucketName = bucketName,
                    //    FilePath = filePath,
                    //    StorageClass = S3StorageClass.StandardInfrequentAccess,
                    //    PartSize = 6291456, // 6 MB.
                    //    Key = keyName,
                    //    CannedACL = S3CannedACL.PublicRead
                    //};
                    //fileTransferUtilityRequest.Metadata.Add("param1", "Value1");
                    //fileTransferUtilityRequest.Metadata.Add("param2", "Value2");

                    //await fileTransferUtility.UploadAsync(fileTransferUtilityRequest);
                    //Console.WriteLine("Upload 4 completed");
                }
                catch (AmazonS3Exception e)
                {
                    Console.WriteLine("Error encountered on server. Message:'{0}' when writing an object", e.Message);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Unknown encountered on server. Message:'{0}' when writing an object", e.Message);
                }

            }
        }

        public int ActualProduction(int i, int prodId)
        {
            var index = GameMain.statistics.production.factoryStatPool[i].productIndices[prodId];
            var production = GameMain.statistics.production.factoryStatPool[i].productPool[index].total[1];
            return Convert.ToInt32(production);
        }

        public int ActualConsumption(int i, int prodId)
        {
            var index = GameMain.statistics.production.factoryStatPool[i].productIndices[prodId];
            var consumption = GameMain.statistics.production.factoryStatPool[i].productPool[index].total[8];
            return Convert.ToInt32(consumption);
        }

        void Update()
        {
            timeCurrent = GameMain.instance.timei;
            if (timeCurrent - timePrev >= 1800)
            {

                jsonList.Clear();
                for (int i = 0; i < GameMain.data.factoryCount; i++)
                {
                    counter.Clear();
                    AddPlanetFactoryData(GameMain.data.factories[i]);
                    foreach (var prodId in counter.Keys)
                    {
                        counter[prodId].metrics.Star = GameMain.data.factories[i].planet.star.ToString();
                        counter[prodId].metrics.Planet = GameMain.data.factories[i].planet.ToString();
                        try
                        {
                            counter[prodId].metrics.Product = LDB.items.Select(prodId).name;
                        }
                        catch
                        {
                            counter[prodId].metrics.Product = "invalid";
                        }
                        counter[prodId].metrics.game_time_elapsed = timeCurrent;
                        counter[prodId].metrics.unique_game_identifier = GameMain.data.gameDesc.galaxySeed.ToString();// + " " + GameMain.data.gameDesc.creationTime.ToString();

                        try
                        {
                            counter[prodId].metrics.production_actual = ActualProduction(i, prodId);
                        }
                        catch
                        {
                            counter[prodId].metrics.production_actual = 0;
                        }

                        try
                        {
                            counter[prodId].metrics.consumption_actual = ActualConsumption(i, prodId);
                        }
                        catch
                        {
                            counter[prodId].metrics.consumption_actual = 0;
                        }

                        counter[prodId].metrics.Product_Planet_elapsed = counter[prodId].metrics.Product + "-" + counter[prodId].metrics.Planet + "-" + counter[prodId].metrics.unique_game_identifier;
                        jsonList.Add(counter[prodId].metrics);
                    }
                }
                Logger.LogInfo(JsonConvert.SerializeObject(jsonList));
                File.WriteAllText(@"d:\DSP_json.json", JsonConvert.SerializeObject(jsonList));
                UploadFileMPUHighLevelAPITest.s3Client = new AmazonS3Client(UploadFileMPUHighLevelAPITest.bucketRegion);
                UploadFileMPUHighLevelAPITest.UploadFileAsync();
                Logger.LogInfo("\nIteration_done\n");
                timePrev = timeCurrent;
            }
        }
    }
}
