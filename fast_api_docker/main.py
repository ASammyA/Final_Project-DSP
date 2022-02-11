from typing import Optional
from fastapi import FastAPI
import boto3
import json

app = FastAPI()

@app.get("/")
def read_root():
    s3 = boto3.resource('s3')
    obj = s3.Object("final-project.dsp.json-file", "DSP_json.json")
    body = obj.get()['Body'].read().decode()
    json_format = json.loads(body)
    return json_format