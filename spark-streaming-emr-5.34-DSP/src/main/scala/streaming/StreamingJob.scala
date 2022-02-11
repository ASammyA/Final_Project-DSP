package weclouddata.streaming

import weclouddata.wrapper.SparkSessionWrapper
import org.apache.spark.sql.execution.datasources.jdbc.JDBCOptions
import org.apache.spark.sql.{DataFrame, SaveMode, SparkSession}
import org.apache.spark.sql.functions._
import org.apache.spark.sql.types.StructType
import org.apache.spark.sql.SaveMode

object StreamingJob extends App with SparkSessionWrapper {  
  def get_schema(path_schema_seed: String) = {
    val df = spark.read.json(path_schema_seed)
    df.schema
  }

  val kafkaReaderConfig = KafkaReaderConfig("b-3.final-project-dsp-msk2.66bk7a.c2.kafka.us-east-1.amazonaws.com:9092,b-4.final-project-dsp-msk2.66bk7a.c2.kafka.us-east-1.amazonaws.com:9092,b-2.final-project-dsp-msk2.66bk7a.c2.kafka.us-east-1.amazonaws.com:9092", "dbserver1.final_project.DSP")
  val schemas  = get_schema("s3://final-project.dsp.schema/DSP_schema.json")
  new StreamingJobExecutor(spark, kafkaReaderConfig, "s3://final-project.dsp.output/checkpoint/job", schemas).execute()
}

case class KafkaReaderConfig(kafkaBootstrapServers: String, topics: String, startingOffsets: String = "latest")

class StreamingJobExecutor(spark: SparkSession, kafkaReaderConfig: KafkaReaderConfig, checkpointLocation: String, schema: StructType) {
  import spark.implicits._

  def read(): DataFrame = {
    spark
      .readStream
      .format("kafka")
      .option("kafka.bootstrap.servers", kafkaReaderConfig.kafkaBootstrapServers)
      .option("subscribe", kafkaReaderConfig.topics)
      .option("startingOffsets", kafkaReaderConfig.startingOffsets)

      .load()
  }

  def execute(): Unit = {
    // read data from kafka and parse them
    val transformDF = read().select(from_json($"value".cast("string"), schema).as("value"))

    transformDF.select($"value.payload.after.*")
                .writeStream
                .option("checkpointLocation", checkpointLocation) 
                .queryName("wcd streaming app")
                .foreachBatch{
                  (batchDF : DataFrame, _: Long) => {
                    //batchDF.cache()
                    batchDF.write.format("org.apache.hudi")
                    .option("hoodie.datasource.write.table.type", "COPY_ON_WRITE")
                    .option("hoodie.datasource.write.recordkey.field", "Product_Planet_elapsed")
                    .option("hoodie.datasource.write.precombine.field", "event_time")
                    .option("hoodie.datasource.write.partitionpath.field", "unique_game_identifier")
                    .option("hoodie.datasource.write.hive_style_partitioning", "true")
                    //.option("hoodie.datasource.hive_sync.jdbcurl", " jdbc:hive2://localhost:10000")
                    .option("hoodie.datasource.hive_sync.database", "final_project_dsp")
                    .option("hoodie.datasource.hive_sync.enable", "true")
                    .option("hoodie.datasource.hive_sync.table", "DSP")
                    .option("hoodie.table.name", "DSP")
                    .option("hoodie.datasource.hive_sync.partition_fields", "unique_game_identifier")
                    .option("hoodie.datasource.hive_sync.partition_extractor_class", "org.apache.hudi.hive.MultiPartKeysValueExtractor")
                    .option("hoodie.upsert.shuffle.parallelism", "100")
                    .option("hoodie.insert.shuffle.parallelism", "100")
                    .mode(SaveMode.Append)
                    .save("s3://final-project.dsp.output/DSP")
                  }
                }
                .start()
                .awaitTermination() 

  }


}
