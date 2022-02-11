name := "wcd-spark-streaming-with-debezium-DSP"

version := "0.1"
scalaVersion := "2.11.11"

val sparkVersion = "2.4.4"


assemblyMergeStrategy in assembly := {
  case "META-INF/services/org.apache.spark.sql.sources.DataSourceRegister" => MergeStrategy.concat
  case PathList("META-INF", xs@_*) => MergeStrategy.discard
  case "application.conf" => MergeStrategy.concat
  case x => MergeStrategy.first
}

val streamingDeps = Seq(
  "org.apache.spark" %% "spark-sql" % sparkVersion,
  "org.apache.spark" %% "spark-sql-kafka-0-10" % sparkVersion,
  "org.apache.kafka" % "kafka-clients" % "2.2.1",
  "org.postgresql" % "postgresql" % "42.2.5",
  "com.typesafe" % "config" % "1.4.0",
  "org.slf4j" % "slf4j-simple" % "1.7.30",
 // "org.apache.hudi" %% "hudi-spark3-bundle" % "0.8.0",
  //"org.apache.spark" %% "spark-avro" % "3.0.1"
)

lazy val root = (project in file(".")).
  settings(
    libraryDependencies ++= streamingDeps 
  )

mainClass in assembly := Some("weclouddata.streaming.StreamingJob")

// exclude Scala library from assembly
assemblyOption in assembly := (assemblyOption in assembly).value.copy(includeScala = false)

assemblyJarName in assembly := s"${name.value}_${scalaVersion.value}-${version.value}.jar"



