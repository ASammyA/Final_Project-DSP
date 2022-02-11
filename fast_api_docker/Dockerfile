FROM python:3.7.10-slim
WORKDIR /code
COPY . .
RUN pip install -r requirements.txt
CMD ["uvicorn", "--host", "0.0.0.0", "main:app"]