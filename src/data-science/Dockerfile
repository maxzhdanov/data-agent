FROM apache/spark:3.5.3
WORKDIR /opt/finguard/data-science
COPY requirements.txt .
COPY . .
CMD ["python3", "anomaly_detection.py"]
