FROM python:3.12-slim
WORKDIR /opt/finguard/data-science
COPY requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt
COPY . ./
CMD ["python", "duckdb_api.py"]
