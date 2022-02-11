package weclouddata.wrapper

import org.apache.spark.sql.SparkSession

/**
 * A trait to get spark sessions.
 */
trait SparkSessionWrapper {

  /*
 Get spark session for local testing
  */
  lazy val spark: SparkSession = SparkSession
    .builder
    .appName("streaming_application")
    .config("spark.serializer", "org.apache.spark.serializer.KryoSerializer")
    .config("spark.hadoop.hive.metastore.client.factory.class", "com.amazonaws.glue.catalog.metastore.AWSGlueDataCatalogHiveClientFactory")
    .enableHiveSupport()
    //to fix issue of port assignment on local
//    .config("spark.driver.bindAddress", "localhost")
    .getOrCreate()

}
